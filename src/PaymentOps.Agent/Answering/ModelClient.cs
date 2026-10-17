using System.ClientModel.Primitives;

using Azure.Core;

using Microsoft.Extensions.AI;

using OpenAI;
using OpenAI.Chat;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// The chat model through Foundry's OpenAI v1 endpoint, signed in with an Entra token. Key
/// authentication is disabled on the resource, so there is no key to pass.
/// </summary>
internal static class ModelClient
{
    public static IChatClient Create(Uri foundryEndpoint, string tokenScope, TokenCredential credential, string deployment, HttpMessageHandler? handler = null)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri(foundryEndpoint, "openai/v1/") };
        if (handler is not null)
        {
            // Tests only: answer from a stub and fail fast instead of retrying.
            options.Transport = new HttpClientPipelineTransport(new HttpClient(handler));
            options.RetryPolicy = new ClientRetryPolicy(maxRetries: 0);
        }

        // The SDK marks its token-based constructor experimental, but it's the only keyless way in.
#pragma warning disable OPENAI001
        return new ChatClient(deployment, new BearerTokenPolicy(credential, tokenScope), options).AsIChatClient();
#pragma warning restore OPENAI001
    }
}
