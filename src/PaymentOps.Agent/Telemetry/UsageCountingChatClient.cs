using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Telemetry;

/// <summary>Adds each response's token usage to the turn and the metrics; a response without usage leaves the turn incomplete.</summary>
internal sealed class UsageCountingChatClient(IChatClient inner, string deployment) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        TurnUsage.RecordModelCall(deployment);
        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch
        {
            // An error response may still have been billed (a filtered prompt), and it carries no usage.
            TurnUsage.RecordIncomplete("model outcome unknown");
            throw;
        }

        Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        TurnUsage.RecordModelCall(deployment);
        UsageDetails? usage = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
            yield return update;
        }

        Record(usage);
    }

    private void Record(UsageDetails? usage)
    {
        TurnUsage.RecordTokens(deployment, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0, usage?.CachedInputTokenCount ?? 0);
        if (usage?.InputTokenCount is null || usage.OutputTokenCount is null)
        {
            TurnUsage.RecordIncomplete("model usage missing");
        }
    }
}
