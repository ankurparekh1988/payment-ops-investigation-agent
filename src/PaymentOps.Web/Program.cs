using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using PaymentOps.Web.Components;
using PaymentOps.Web.Health;
using PaymentOps.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// In Azure this resolves to the app's managed identity (AZURE_CLIENT_ID); locally, the developer's sign-in.
TokenCredential azureCredential = new DefaultAzureCredential();
builder.Services.AddSingleton(azureCredential);

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

// Readiness depends on Foundry once an endpoint is configured; locally and in tests it isn't.
var foundry = builder.Configuration.GetSection(FoundryOptions.SectionName).Get<FoundryOptions>();
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
