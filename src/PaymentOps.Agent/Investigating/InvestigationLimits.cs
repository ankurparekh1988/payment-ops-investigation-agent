namespace PaymentOps.Agent.Investigating;

/// <summary>Limits on one investigation. Enforced by our own code; the framework's iteration cap is only a backstop.</summary>
public sealed class InvestigationLimits
{
    public const string SectionName = "PaymentOps:Investigation";

    /// <summary>Read-tool calls per turn; <c>submit_assessment</c> isn't counted.</summary>
    public int MaxToolCalls { get; set; } = 10;

    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Calls with invalid arguments the model may correct before the turn stops.</summary>
    public int MaxCorrections { get; set; } = 2;

    /// <summary>Identical calls allowed; one more stops the turn.</summary>
    public int MaxRepeats { get; set; } = 2;

    public int MaxQuestionLength { get; set; } = 1000;

    public int MaxOutputTokens { get; set; } = 2000;
}
