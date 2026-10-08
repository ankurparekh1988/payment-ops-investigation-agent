using Azure.Core;
using Azure.Identity;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using PaymentOps.Web.Components;
using PaymentOps.Web.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var healthChecks = builder.Services.AddHealthChecks();

// Readiness depends on Foundry once an endpoint is configured; locally and in tests it isn't.
var foundry = builder.Configuration.GetSection(FoundryOptions.SectionName).Get<FoundryOptions>();
if (foundry?.Endpoint is { } foundryEndpoint)
{
    // In Azure this resolves to the app's managed identity (AZURE_CLIENT_ID); locally, the developer's sign-in.
    builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
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

app.UseAntiforgery();

// Liveness: the process is up. Probed by availability tests, so it touches no dependencies.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness: the app can reach the services it depends on. Used by the deployment smoke test.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
