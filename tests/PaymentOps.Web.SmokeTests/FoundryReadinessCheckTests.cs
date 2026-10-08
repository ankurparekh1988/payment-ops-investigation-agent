using System.Net;

using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using PaymentOps.Web.Health;

namespace PaymentOps.Web.SmokeTests;

public sealed class FoundryReadinessCheckTests
{
    private static readonly Uri Endpoint = new("https://foundry.example.test/");
    private const string TokenScope = "https://ai.example.test/.default";

    [Fact]
    public async Task Healthy_when_foundry_accepts_the_identity_token()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var credential = new FixedTokenCredential();

        var result = await RunCheck(handler, credential);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal([TokenScope], credential.RequestedScopes);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("https://foundry.example.test/openai/v1/models", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task Unhealthy_when_foundry_rejects_the_identity()
    {
        var result = await RunCheck(new RecordingHandler(HttpStatusCode.Unauthorized), new FixedTokenCredential());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("401", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhealthy_when_no_token_can_be_obtained()
    {
        var result = await RunCheck(new RecordingHandler(HttpStatusCode.OK), new FailingTokenCredential());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<AuthenticationFailedException>(result.Exception);
    }

    private static async Task<HealthCheckResult> RunCheck(RecordingHandler handler, TokenCredential credential)
    {
        using var httpClient = new HttpClient(handler);
        var check = new FoundryReadinessCheck(httpClient, credential, Endpoint, TokenScope);
        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class FixedTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public string[] RequestedScopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            RequestedScopes = requestContext.Scopes;
            return ValueTask.FromResult(Token);
        }
    }

    private sealed class FailingTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new AuthenticationFailedException("No managed identity available.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new AuthenticationFailedException("No managed identity available.");
    }
}
