namespace PaymentOps.Web.Usage;

/// <summary>How much investigating the deployment allows: a pause switch and daily limits, set by the deployment.</summary>
public sealed class UsageOptions
{
    public const string SectionName = "PaymentOps:Usage";

    /// <summary>When true, nothing is investigated and nothing is counted.</summary>
    public bool Paused { get; set; }

    /// <summary>Investigations one person may start in a UTC day.</summary>
    public int DailyLimitPerUser { get; set; }

    /// <summary>Investigations everyone together may start in a UTC day.</summary>
    public int DailyLimitTotal { get; set; }

    /// <summary>The table service endpoint holding the counts; only Development may run without one.</summary>
    public Uri? TableEndpoint { get; set; }
}

internal enum UsageReservation
{
    Admitted,
    UserLimitReached,
    TotalLimitReached,
}

/// <summary>Today's usage couldn't be read or written, so whether a limit was reached is unknown.</summary>
internal sealed class UsageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reserves one investigation for a person on a UTC day, against both limits, or refuses.</summary>
internal interface IUsageCounter
{
    /// <exception cref="UsageUnavailableException">The counts couldn't be read or written.</exception>
    Task<UsageReservation> TryReserveAsync(string userId, DateOnly day, int perUserLimit, int totalLimit, CancellationToken cancellationToken);

    /// <summary>The person's and everyone's count for the day, reserving nothing.</summary>
    /// <exception cref="UsageUnavailableException">The counts couldn't be read.</exception>
    Task<(int User, int Total)> ReadAsync(string userId, DateOnly day, CancellationToken cancellationToken);
}
