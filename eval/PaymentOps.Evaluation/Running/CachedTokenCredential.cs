using System.Collections.Concurrent;

using Azure.Core;

namespace PaymentOps.Evaluation.Running;

/// <summary>
/// One token per scope for the whole evaluation. Every case gets fresh clients, and the Azure CLI credential caches
/// nothing, so without this each case would start <c>az</c> inside its timed window and inflate every latency.
/// A token carries no case state, so sharing it keeps the cases independent.
/// </summary>
internal sealed class CachedTokenCredential(TokenCredential inner) : TokenCredential
{
    // Refreshed well before expiry, so a token never runs out in the middle of a case.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, AccessToken> _tokens = new(StringComparer.Ordinal);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        Cached(requestContext, out var token) ? token : Keep(requestContext, inner.GetToken(requestContext, cancellationToken));

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        Cached(requestContext, out var token) ? token : Keep(requestContext, await inner.GetTokenAsync(requestContext, cancellationToken));

    private static string Key(TokenRequestContext context) => string.Join(' ', context.Scopes) + "|" + context.TenantId;

    private bool Cached(TokenRequestContext context, out AccessToken token) =>
        _tokens.TryGetValue(Key(context), out token) && token.ExpiresOn - RefreshMargin > DateTimeOffset.UtcNow;

    private AccessToken Keep(TokenRequestContext context, AccessToken token) => _tokens[Key(context)] = token;
}
