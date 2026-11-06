namespace PaymentOps.Tools;

/// <summary>
/// A lookup the facts in a result call for, as the runbooks list it. It's in the result so the agent sees it while
/// choosing what to check next; the agent still decides whether to make it.
/// </summary>
public sealed record NextCheck(string Tool, string ProcessorId, DateOnly StartDate, DateOnly EndDate, NextCheckReason Because)
{
    /// <summary>One check per processor and reason, spanning every date that called for it.</summary>
    public static IReadOnlyList<NextCheck> ErrorEvents(IEnumerable<(string ProcessorId, DateOnly Start, DateOnly End, NextCheckReason Because)> found) =>
        [.. found.GroupBy(f => (f.ProcessorId, f.Because))
            .Select(g => new NextCheck(ToolNames.ErrorEvents, g.Key.ProcessorId, g.Min(f => f.Start), g.Max(f => f.End), g.Key.Because))];
}

public enum NextCheckReason
{
    MissingFile,
    PaymentsInMoreThanOneFile,
    Incident,
}
