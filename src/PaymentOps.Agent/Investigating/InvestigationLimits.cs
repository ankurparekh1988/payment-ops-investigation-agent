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
    public TimeSpan ToolResultBudget { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>How long Prompt Shields may take, within what remains of <see cref="ToolResultBudget"/>.</summary>
    public TimeSpan ScreenTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Calls with invalid arguments the model may correct before the turn stops.</summary>
    public int MaxCorrections { get; set; } = 2;

    /// <summary>Identical calls allowed; one more stops the turn.</summary>
    public int MaxRepeats { get; set; } = 2;

    public int MaxQuestionLength { get; set; } = 1000;

    public int MaxOutputTokens { get; set; } = 2000;
}
