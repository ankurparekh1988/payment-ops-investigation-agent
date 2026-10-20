namespace PaymentOps.Data.Demo.Faults;

/// <summary>How a port misbehaves when fault injection is on. Every call behaves the same, so runs repeat.</summary>
public enum FaultMode
{
    None,

    /// <summary>Answers correctly after <see cref="FaultOptions.Delay"/>.</summary>
    Delay,

    /// <summary>Never answers; returns only when the caller's token is cancelled.</summary>
    Hang,

    /// <summary>Answers with nothing found.</summary>
    Empty,

    /// <summary>Fails as a transient outage.</summary>
    Unavailable,
}
