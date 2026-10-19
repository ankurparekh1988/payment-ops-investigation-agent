using Azure;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Web.Health;

namespace PaymentOps.Web.SmokeTests;

public sealed class SearchReadinessCheckTests
{
    [Fact]
    public async Task Reachable_search_is_healthy()
    {
        var result = await new SearchReadinessCheck(_ => Task.CompletedTask).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task A_forbidden_query_is_unhealthy_and_names_the_status()
    {
        var check = new SearchReadinessCheck(_ => throw new KnowledgeUnavailableException(new RequestFailedException(403, "Forbidden")));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("403", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_service_is_unhealthy()
    {
        var check = new SearchReadinessCheck(_ => throw new HttpRequestException("no route"));

        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }
}
