using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using PaymentOps.Agent;
using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Safety;
using PaymentOps.Data.Demo;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;
using PaymentOps.Web;
using PaymentOps.Web.Components;
using PaymentOps.Web.Health;
using PaymentOps.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// The AI release is part of this build. It's verified before anything else, so an inconsistent
// manifest or prompt stops startup instead of failing on a later request.
var aiRelease = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());
builder.Services.AddSingleton(aiRelease);
builder.Services.AddSingleton<IPromptProvider, PromptProvider>();

// The demo systems of record, also built in and checked at startup. Fault injection stays off here.
builder.Services.AddDemoData(builder.Configuration);

// In Azure this resolves to the app's managed identity (AZURE_CLIENT_ID); locally, the developer's sign-in.
TokenCredential azureCredential = new DefaultAzureCredential();
builder.Services.AddSingleton(azureCredential);

builder.Services.AddOptions<InvestigationLimits>()
    .Bind(builder.Configuration.GetSection(InvestigationLimits.SectionName))
    .Validate(
        o => o.MaxToolCalls > 0 && o.MaxCorrections >= 0 && o.MaxRepeats > 0 && o.MaxQuestionLength > 0 && o.MaxOutputTokens > 0
            && o.ToolTimeout > TimeSpan.Zero && o.ToolTimeout <= TimeSpan.FromMinutes(5)
            && o.TurnTimeout > TimeSpan.Zero && o.TurnTimeout <= TimeSpan.FromMinutes(5),
        $"{InvestigationLimits.SectionName} needs positive limits and timeouts of at most 5 minutes.")
    .ValidateOnStart();
builder.Services.AddOptions<ConversationOptions>()
    .Bind(builder.Configuration.GetSection(ConversationOptions.SectionName))
    .Validate(
        o => o.InputBudgetTokens > 0 && o.ToolResultAllowanceTokens > 0 && o.MaxQuestions > 0,
        $"{ConversationOptions.SectionName} needs a positive input budget, tool result allowance and question limit.")
    .ValidateOnStart();
builder.Services.AddOptions<ApprovalOptions>()
    .Bind(builder.Configuration.GetSection(ApprovalOptions.SectionName))
    .Validate(
        o => o.ProposalLifetime > TimeSpan.Zero && o.ProposalLifetime <= TimeSpan.FromHours(1) && o.MaxPendingProposals > 0,
        $"{ApprovalOptions.SectionName} needs a proposal lifetime of at most an hour and a positive pending limit.")
    .ValidateOnStart();
builder.Services.AddOptions<ToolOptions>()
    .Bind(builder.Configuration.GetSection(ToolOptions.SectionName))
    .Validate(
        o => o.MaxListItems > 0 && o.MaxQueryLength > 0 && o.RetryBackoff >= TimeSpan.Zero && o.RetryBackoff <= TimeSpan.FromSeconds(10),
        $"{ToolOptions.SectionName} needs positive limits and a retry backoff of at most 10 seconds.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => new QuestionLimits(sp.GetRequiredService<IOptions<InvestigationLimits>>().Value.MaxQuestionLength));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddPaymentOpsSecurity(builder.Configuration);

// Telemetry goes to Application Insights when it's configured. Its local authentication is
// disabled, so the exporter signs in with the app's identity.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.Credential = azureCredential);
}

var healthChecks = builder.Services.AddHealthChecks();
var foundry = builder.Configuration.GetSection(FoundryOptions.SectionName).Get<FoundryOptions>();
var search = builder.Configuration.GetSection(SearchOptions.SectionName).Get<SearchOptions>();

// Both endpoints come from Terraform in Azure. Without them (locally, in tests) questions can't be
// answered; outside Development that's a deployment mistake, so startup fails.
if (search?.Endpoint is { } searchEndpoint && foundry?.Endpoint is { } answeringEndpoint)
{
    // Screening untrusted tool text needs the Content Safety endpoint; without it everything untrusted is withheld.
    if (!builder.Environment.IsDevelopment() && builder.Configuration.GetSection(ContentSafetyOptions.SectionName).Get<ContentSafetyOptions>()?.Endpoint is null)
    {
        throw new InvalidOperationException($"{ContentSafetyOptions.EndpointKey} is required outside Development.");
    }

    builder.Services.AddInvestigation(new InvestigationEndpoints(searchEndpoint, answeringEndpoint, foundry.TokenScope));
    healthChecks.Add(new HealthCheckRegistration(
        "search",
        services => new SearchReadinessCheck(services.GetRequiredService<KnowledgeRetriever>().CheckReachableAsync),
        failureStatus: null,
        tags: ["ready"]));
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException($"{SearchOptions.SectionName}:Endpoint and {FoundryOptions.SectionName}:Endpoint are required outside Development.");
}
else
{
    builder.Services.AddScoped<IInvestigationService, UnconfiguredInvestigationService>();
    builder.Services.AddScoped<IApprovalService, UnconfiguredApprovalService>();
}

// Readiness depends on Foundry once an endpoint is configured; locally and in tests it isn't.
if (foundry?.Endpoint is { } foundryEndpoint)
{
    builder.Services.AddHttpClient(nameof(FoundryReadinessCheck), client => client.Timeout = TimeSpan.FromSeconds(10));

    healthChecks.Add(new HealthCheckRegistration(
        "foundry",
        services => new FoundryReadinessCheck(
            services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(FoundryReadinessCheck)),
            services.GetRequiredService<TokenCredential>(),
            foundryEndpoint,
            foundry.TokenScope),
        failureStatus: null,
        tags: ["ready"]));
}

var app = builder.Build();

// Telemetry exports only once the host has started, so the release is logged then.
app.Lifetime.ApplicationStarted.Register(() => AiReleaseLog.Loaded(app.Logger, aiRelease));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAntiforgery();

// Liveness: the process is up. Probed by availability tests, so it touches no dependencies.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

// Readiness: the app can reach the services it depends on. Used by the deployment smoke test.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

app.MapAccountEndpoints();
app.MapStaticAssets().AllowAnonymous();
// The page approves tickets, so no site may frame it: a framed page could trick a click.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = "'none'");

app.Run();
