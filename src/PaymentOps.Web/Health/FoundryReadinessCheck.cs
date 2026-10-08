using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PaymentOps.Web.Health;

/// <summary>
/// Confirms the app can reach Microsoft Foundry with its own identity. Listing the account's models
/// consumes no tokens, so the check is safe to run on every readiness probe.
/// </summary>
internal sealed class FoundryReadinessCheck(HttpClient httpClient, TokenCredential credential, Uri endpoint, string tokenScope)
    : IHealthCheck
{
    private readonly string[] _scopes = [tokenScope];

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "openai/v1/models"));
            request.Headers.Authorization = new("Bearer", token.Token);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Foundry returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Foundry is unreachable.", ex);
        }
    }
}
