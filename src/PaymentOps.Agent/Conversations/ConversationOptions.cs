namespace PaymentOps.Agent.Conversations;

/// <summary>
/// The application's own input budget for one request, not a statement of the model's context window.
/// History gets only what's left after the current turn's reservations.
/// </summary>
public sealed class ConversationOptions
{
    public const string SectionName = "PaymentOps:Conversation";

    public int InputBudgetTokens { get; set; } = 32_000;

    /// <summary>
    /// Reserved for each read-tool result the current turn may receive. The largest result on the shipped data,
    /// 31 days of settlement batches at the list cap, is about 2,260 tokens by the same estimate.
    /// </summary>
    public int ToolResultAllowanceTokens { get; set; } = 2_300;

    public int MaxQuestions { get; set; } = 20;
}
