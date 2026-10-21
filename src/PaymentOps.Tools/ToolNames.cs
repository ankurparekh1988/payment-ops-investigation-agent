namespace PaymentOps.Tools;

/// <summary>The names the model calls tools by. Public: they're part of the agent's contract and its evaluation.</summary>
public static class ToolNames
{
    public const string MerchantProfile = "get_merchant_profile";
    public const string SettlementBatches = "get_settlement_batches";
    public const string ProcessorStatus = "get_processor_status";
    public const string ErrorEvents = "get_error_events";
    public const string KnowledgeSearch = "search_knowledge";

    /// <summary>The agent's terminal tool; it reads nothing, so it's not among the read tools.</summary>
    public const string SubmitAssessment = "submit_assessment";

    public static IReadOnlyList<string> ReadTools { get; } = [MerchantProfile, SettlementBatches, ProcessorStatus, ErrorEvents, KnowledgeSearch];
}
