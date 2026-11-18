using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Checks each logical model call against the investigation's budget before it's sent, and charges it when it returns.
/// Sits above the model client, so the SDK's retries of one call stay one logical call.
/// </summary>
internal sealed class BudgetedChatClient(IChatClient inner, InvestigationBudget budget) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var sent = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        var admission = budget.Admit(sent, options);
        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(sent, options, cancellationToken);
        }
        catch
        {
            // It may still have been billed, and nothing says how much.
            budget.Charge(admission, null);
            throw;
        }

        budget.Charge(admission, response.Usage);
        return response;
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("An investigation's model calls aren't streamed, so each is budgeted whole.");
}
