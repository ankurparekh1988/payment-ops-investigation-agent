using PaymentOps.Tools;

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
/// so it can't hide one; people see this fixed wording, once however many tools hit it.
/// </summary>
internal sealed record ObservedGap(ObservedGapKind Kind, string Wording)
{
    public static ObservedGap Unavailable(string source) => new(ObservedGapKind.Unavailable, $"{source} was unavailable.");

    public static ObservedGap Timeout(string source) => new(ObservedGapKind.Timeout, $"{source} didn't respond in time.");

    public static ObservedGap Truncated(Truncation truncation) =>
        new(ObservedGapKind.Truncated, $"{truncation.What}: only the first {truncation.Shown} of {truncation.Total} were shown.");

    public static ObservedGap NotAuthorized(string tool) => new(ObservedGapKind.NotAuthorized, $"{tool} wasn't available to this user.");

    public static ObservedGap LimitReached { get; } = new(ObservedGapKind.LimitReached, "The investigation stopped at its step limit.");
}
