namespace PaymentOps.Agent.Investigating;

internal enum ObservedGapKind
{
    Unavailable,
    Timeout,
    Truncated,
    NotAuthorized,
    LimitReached,
}

/// <summary>
/// Something the turn couldn't check, recorded by server code as it happened. The model never reports gaps,
/// so it can't hide one; people see this fixed wording.
/// </summary>
internal sealed record ObservedGap(ObservedGapKind Kind, string Tool, string Wording)
{
    public static ObservedGap Unavailable(string tool, string source) => new(ObservedGapKind.Unavailable, tool, $"{source} was unavailable.");

    public static ObservedGap Timeout(string tool, string source) => new(ObservedGapKind.Timeout, tool, $"{source} didn't respond in time.");

    public static ObservedGap Truncated(string tool, PaymentOps.Tools.Truncation truncation) =>
        new(ObservedGapKind.Truncated, tool, $"{truncation.What} was cut to the first {truncation.Shown} of {truncation.Total}.");

    public static ObservedGap NotAuthorized(string tool) => new(ObservedGapKind.NotAuthorized, tool, $"{tool} wasn't available to this user.");

    public static ObservedGap LimitReached(string tool) => new(ObservedGapKind.LimitReached, tool, "The investigation stopped at its step limit.");

    /// <summary>A source that failed, as opposed to a result that was only shortened.</summary>
    public bool IsFailure => Kind is ObservedGapKind.Unavailable or ObservedGapKind.Timeout or ObservedGapKind.NotAuthorized;
}
