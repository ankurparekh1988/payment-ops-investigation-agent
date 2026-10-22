namespace PaymentOps.Agent.Contracts;

/// <summary>
/// What the agent is doing, written by the server as it happens. Labels come from parsed tool arguments,
/// never from model text, so nothing the model writes reaches the timeline.
/// </summary>
public abstract record InvestigationProgress
{
    private InvestigationProgress()
    {
    }

    public sealed record ToolStarted(string Tool, string Label) : InvestigationProgress;

    /// <param name="Records">Records or sections the call returned; computed facts aren't counted.</param>
    /// <param name="Gap">The fixed wording of what the call couldn't check, if anything.</param>
    public sealed record ToolFinished(string Tool, string Label, bool Succeeded, int Records, string? Gap) : InvestigationProgress;

    /// <summary>The submission didn't check out and went back to the model once.</summary>
    public sealed record CorrectionRequested : InvestigationProgress;

    public sealed record LimitReached : InvestigationProgress;

    public sealed record Submitting : InvestigationProgress;
}
