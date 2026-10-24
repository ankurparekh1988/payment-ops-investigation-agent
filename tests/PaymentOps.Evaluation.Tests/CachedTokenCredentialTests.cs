using Azure.Core;

using PaymentOps.Evaluation.Running;

namespace PaymentOps.Evaluation.Tests;

public sealed class CachedTokenCredentialTests
{
    private static readonly TokenRequestContext Foundry = new(["https://cognitiveservices.azure.com/.default"]);
    private static readonly TokenRequestContext Search = new(["https://search.azure.com/.default"]);

    [Fact]
    public async Task A_token_is_fetched_once_per_scope_until_it_nears_expiry()
    {
        var inner = new CountingCredential(TimeSpan.FromHours(1));
        var credential = new CachedTokenCredential(inner);

        await credential.GetTokenAsync(Foundry, CancellationToken.None);
        await credential.GetTokenAsync(Foundry, CancellationToken.None);
        credential.GetToken(Search, CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task A_token_close_to_expiry_is_fetched_again()
    {
        var inner = new CountingCredential(TimeSpan.FromMinutes(2));
        var credential = new CachedTokenCredential(inner);

        await credential.GetTokenAsync(Foundry, CancellationToken.None);
        await credential.GetTokenAsync(Foundry, CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    // A stand-in for the Azure CLI: hands out a fake token string and counts how often it was asked.
    private sealed class CountingCredential(TimeSpan lifetime) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new($"token-{++Calls}", DateTimeOffset.UtcNow + lifetime);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
