namespace PaymentOps.Agent.Investigating;

/// <summary>Limits on one investigation. Enforced by our own code; the framework's iteration cap is only a backstop.</summary>
public sealed class InvestigationLimits
{
    public const string SectionName = "PaymentOps:Investigation";

    /// <summary>Read-tool calls per turn; <c>submit_assessment</c> isn't counted.</summary>
    public int MaxToolCalls { get; set; } = 10;

    /// <summary>How long the tool or data source itself may take.</summary>
    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The whole guarded call, from starting the tool to finishing its screening; budgets the screening, not the source.</summary>
    public TimeSpan ToolResultBudget { get; set; } = TimeSpan.FromSeconds(13);

    /// <summary>
    /// How long screening a tool result may take, within what remains of <see cref="ToolResultBudget"/>. The harm checks
    /// make one call per value and wait for the slowest, so a result with many values takes seconds; as for a question.
    /// </summary>
    public TimeSpan ScreenTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long a question's three safety checks may take together, before the investigation agent sees it. The classifier
    /// is a model call of about 2 s, so a tighter budget would turn a slow moment into questions that can't be investigated.
    /// </summary>
    public TimeSpan QuestionScreenTimeout { get; set; } = TimeSpan.FromSeconds(8);

    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Calls with invalid arguments the model may correct before the turn stops.</summary>
    public int MaxCorrections { get; set; } = 2;

    /// <summary>Identical calls allowed; one more stops the turn.</summary>
    public int MaxRepeats { get; set; } = 2;

    public int MaxQuestionLength { get; set; } = 1000;

    public int MaxOutputTokens { get; set; } = 2000;

    /// <summary>Logical model calls per investigation across its attempts; the SDK's retries of one call aren't counted.</summary>
    public int MaxModelRequests { get; set; } = 16;

    /// <summary>
    /// Input and output tokens per investigation, checked before each model call against what's been used plus an estimate
    /// of that call: an estimated allowance, not an absolute ceiling.
    /// </summary>
    public int MaxInvestigationTokens { get; set; } = 120_000;

    /// <summary>What must be left for a second attempt to start (ADR 0023); otherwise the first attempt's result stands.</summary>
    public TimeSpan MinEscalationTime { get; set; } = TimeSpan.FromSeconds(20);

    public int MinEscalationToolCalls { get; set; } = 2;

    public int MinEscalationModelRequests { get; set; } = 8;

    public int MinEscalationTokens { get; set; } = 60_000;
}
