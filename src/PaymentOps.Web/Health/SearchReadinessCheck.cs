using Azure.Identity;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Web.Health;

/// <summary>
/// Confirms the app's identity can query the knowledge index. It asks for no documents, so it proves
/// configuration and access, not that any particular content is there.
/// </summary>
internal sealed class SearchReadinessCheck(Func<CancellationToken, Task> probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await probe(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (KnowledgeUnavailableException ex)
        {
            return HealthCheckResult.Unhealthy(ex.Status is { } status ? $"Search returned {status}." : "Search is unreachable.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or AuthenticationFailedException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Search is unreachable.", ex);
        }
    }
}
