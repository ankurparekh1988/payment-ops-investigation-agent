using System.Collections.Concurrent;

using Azure.Core;
using Azure.Identity;

namespace PaymentOps.Evaluation.Running;

/// <summary>
/// One token per scope for the whole evaluation. Every case gets fresh clients, and the Azure CLI credential caches
/// nothing, so without this each case would start <c>az</c> inside its timed window and inflate every latency.
/// A token carries no case state, so sharing it keeps the cases independent.
/// </summary>
/// <param name="signInFailed">Told of each refused sign-in, which a screen would otherwise only report as unavailable.</param>
internal sealed class CachedTokenCredential(TokenCredential inner, Action? signInFailed = null) : TokenCredential
{
    // Refreshed well before expiry, so a token never runs out in the middle of a case.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, AccessToken> _tokens = new(StringComparer.Ordinal);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (Cached(requestContext, out var token))
        {
            return token;
        }

        try
        {
            return Keep(requestContext, inner.GetToken(requestContext, cancellationToken));
        }
        catch (AuthenticationFailedException)
        {
            signInFailed?.Invoke();
            throw;
        }
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (Cached(requestContext, out var token))
        {
            return token;
        }

        try
        {
            return Keep(requestContext, await inner.GetTokenAsync(requestContext, cancellationToken));
        }
        catch (AuthenticationFailedException)
        {
            signInFailed?.Invoke();
            throw;
        }
    }

    private static string Key(TokenRequestContext context) => string.Join(' ', context.Scopes) + "|" + context.TenantId;

    private bool Cached(TokenRequestContext context, out AccessToken token) =>
        _tokens.TryGetValue(Key(context), out token) && token.ExpiresOn - RefreshMargin > DateTimeOffset.UtcNow;

    private AccessToken Keep(TokenRequestContext context, AccessToken token) => _tokens[Key(context)] = token;
}
