using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.SmokeTests;

public sealed class InvestigationWiringTests
{
    [Fact]
    public void With_both_endpoints_the_investigation_and_search_readiness_are_wired()
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => b
            .UseSetting("PaymentOps:Search:Endpoint", "https://search.example")
            .UseSetting("PaymentOps:Foundry:Endpoint", "https://foundry.example/"));

        using var scope = configured.Services.CreateScope();

        Assert.IsNotType<UnconfiguredInvestigationService>(scope.ServiceProvider.GetRequiredService<IInvestigationService>());
        var checks = configured.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Contains(checks, c => c.Name == "search" && c.Tags.Contains("ready"));
    }

    [Fact]
    public void Without_endpoints_development_reports_the_service_as_unavailable()
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<UnconfiguredInvestigationService>(scope.ServiceProvider.GetRequiredService<IInvestigationService>());
    }
}
