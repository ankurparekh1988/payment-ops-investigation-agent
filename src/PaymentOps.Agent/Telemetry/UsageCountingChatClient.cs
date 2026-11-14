using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Telemetry;

/// <summary>Adds each response's token usage to the turn and the metrics; a response without usage leaves the turn incomplete.</summary>
internal sealed class UsageCountingChatClient(IChatClient inner, string deployment) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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
        if (usage is { InputTokenCount: { } input, OutputTokenCount: { } output })
        {
            TurnUsage.RecordTokens(deployment, input, output);
            return;
        }

        TurnUsage.RecordTokens(deployment, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);
        TurnUsage.RecordIncomplete("model usage missing");
    }
}
