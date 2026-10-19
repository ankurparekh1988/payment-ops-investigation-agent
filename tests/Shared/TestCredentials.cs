using Azure.Core;

namespace PaymentOps.Tests.Shared;

/// <summary>A token that never leaves the test process. Key authentication is disabled, so tests don't use keys either.</summary>
internal sealed class StaticTokenCredential : TokenCredential
{
    public List<string> Scopes { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        Scopes.AddRange(requestContext.Scopes);
        return new("test-token", DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}

/// <summary>A credential whose token request fails, as a managed-identity endpoint can transiently.</summary>
internal sealed class FailingTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new Azure.Identity.AuthenticationFailedException("The managed identity endpoint didn't respond.");

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new Azure.Identity.AuthenticationFailedException("The managed identity endpoint didn't respond.");
}
