using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Azure.Core;
using Azure.Identity;

namespace PaymentOps.Agent.Safety;

/// <summary>
/// Calls the Foundry account's Content Safety REST operations with an Entra token, never a key. Any failure of the
/// service comes back as null, so a caller can treat it as "couldn't be checked"; only the caller's cancellation throws.
/// </summary>
internal sealed class ContentSafetyClient(HttpClient http, Uri endpoint, string tokenScope, TokenCredential credential)
{
    // Managed identity caches its tokens, but a developer's Azure CLI sign-in starts a process for each one, which
    // would spend the screening budget. Two calls refreshing at once both fetch one; that's harmless.
    private volatile CachedToken? _token;

    /// <param name="operation">The operation's path under the endpoint, such as <c>contentsafety/text:analyze</c>.</param>
    public async Task<JsonDocument?> PostAsync(string operation, object body, CancellationToken cancellationToken)
    {
        try
        {
            var token = await TokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"{operation}?api-version=2024-09-01")) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                : null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or TaskCanceledException or AuthenticationFailedException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (_token is { } cached && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return cached.Value;
        }

        var fresh = await credential.GetTokenAsync(new TokenRequestContext([tokenScope]), cancellationToken);
        _token = new CachedToken(fresh.Token, fresh.ExpiresOn);
        return fresh.Token;
    }

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresOn);
}
