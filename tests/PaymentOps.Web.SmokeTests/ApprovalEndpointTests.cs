using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentOps.Web.SmokeTests;

public sealed class ApprovalEndpointTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    // Decisions go through the signed-in circuit only, so there's no request another site could forge.
    [Fact]
    public void No_endpoint_accepts_approvals()
    {
        using var client = factory.CreateClient();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToList();

        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, r => r.Contains("approv", StringComparison.OrdinalIgnoreCase) || r.Contains("ticket", StringComparison.OrdinalIgnoreCase));
    }
}
