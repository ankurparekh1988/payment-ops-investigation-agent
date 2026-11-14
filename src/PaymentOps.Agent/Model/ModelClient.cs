using System.ClientModel.Primitives;

using Azure.Core;

using Microsoft.Extensions.AI;

using OpenAI;
using OpenAI.Chat;

using PaymentOps.Agent.Telemetry;

namespace PaymentOps.Agent.Model;

/// <summary>
/// The chat model through Foundry's OpenAI v1 endpoint, signed in with an Entra token. Key
/// authentication is disabled on the resource, so there is no key to pass.
/// </summary>
internal static class ModelClient
{
    public static IChatClient Create(Uri foundryEndpoint, string tokenScope, TokenCredential credential, string deployment, HttpMessageHandler? handler = null)
    {
        // The SDK marks its token-based constructor experimental, but it's the only keyless way in.
#pragma warning disable OPENAI001
        var model = new ChatClient(deployment, new BearerTokenPolicy(credential, tokenScope), Options(foundryEndpoint, deployment, handler)).AsIChatClient();
#pragma warning restore OPENAI001
        return new UsageCountingChatClient(model, deployment);
    }

    /// <summary>
    /// Counting runs per attempt, beneath the SDK's retries, so every request is seen; transport and retries stay the
    /// SDK's own unless a test supplies a stub.
    /// </summary>
    internal static OpenAIClientOptions Options(Uri foundryEndpoint, string deployment, HttpMessageHandler? handler)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(foundryEndpoint, "openai/v1/") };
        options.AddPolicy(new RequestCountingPolicy(deployment), PipelinePosition.PerTry);
        if (handler is not null)
        {
            // Tests only: answer from a stub, with one retry so retry behaviour stays exercised.
            options.Transport = new HttpClientPipelineTransport(new HttpClient(handler));
            options.RetryPolicy = new ClientRetryPolicy(maxRetries: 1);
        }

        return options;
    }
}
