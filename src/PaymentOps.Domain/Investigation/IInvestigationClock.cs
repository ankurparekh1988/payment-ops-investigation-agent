namespace PaymentOps.Domain.Investigation;

/// <summary>
/// "Now" for an investigation: the moment the systems of record are read as of. Separate from the app's
/// own clock, so sign-in and session timeouts keep real time when the data is a fixed snapshot.
/// </summary>
public interface IInvestigationClock
{
    DateTimeOffset Now { get; }

    /// <summary>The UTC date of <see cref="Now"/>; "yesterday" in a question is the day before it.</summary>
    DateOnly Today { get; }
}
