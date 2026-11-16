namespace PaymentOps.Web.Usage;

/// <summary>Whether an Admin has paused investigations, and the last change: who, when and why.</summary>
/// <param name="ChangedBy">The pseudonymous id the usage rows use, never the account id.</param>
/// <param name="ChangedByName">The display name, shown to Admins in the last-change line; never logged.</param>
/// <param name="Reason">Optional plain text, up to <see cref="PauseSwitch.MaxReasonLength"/> characters.</param>
public sealed record PauseState(bool Paused, string? ChangedBy, string? ChangedByName, DateTimeOffset? ChangedAt, string? Reason)
{
    public static PauseState Running { get; } = new(false, null, null, null, null);

    /// <summary>The stored version this state was read at; a write made from it fails if the row has changed since.</summary>
    public string? Version { get; init; }
}

/// <summary>What a pause or resume did. The panel shows what the server answered, never what the click hoped for.</summary>
public abstract record PauseResult
{
    private PauseResult()
    {
    }

    public sealed record Changed(PauseState State) : PauseResult;

    public sealed record NotAuthorized : PauseResult;

    /// <summary>Someone else changed it first, or it was already in the asked-for state; this is the state now.</summary>
    public sealed record Conflict(PauseState Current) : PauseResult;

    /// <summary>The change couldn't be stored; nothing changed.</summary>
    public sealed record Unavailable : PauseResult;
}

/// <summary>Pauses and resumes investigations for everyone. Authorization is checked here, on every call.</summary>
public interface IPauseSwitch
{
    /// <exception cref="PauseUnavailableException">The pause couldn't be read.</exception>
    Task<PauseState> GetAsync(CancellationToken cancellationToken);

    Task<PauseResult> SetAsync(bool paused, string? reason, CancellationToken cancellationToken);
}

/// <summary>The pause couldn't be read, so whether investigations are paused is unknown.</summary>
public sealed class PauseUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

internal abstract record PauseWrite
{
    private PauseWrite()
    {
    }

    public sealed record Written(PauseState State) : PauseWrite;

    public sealed record Conflict(PauseState Current) : PauseWrite;

    public sealed record Failed : PauseWrite;
}

/// <summary>Where the pause is kept, shared by every instance. Reads may be cached; a fresh read never is.</summary>
internal interface IPauseStore
{
    /// <exception cref="PauseUnavailableException">The pause couldn't be read.</exception>
    Task<PauseState> ReadAsync(CancellationToken cancellationToken);

    /// <exception cref="PauseUnavailableException">The pause couldn't be read.</exception>
    Task<PauseState> ReadFreshAsync(CancellationToken cancellationToken);

    /// <summary>Writes the state if the stored one is still at <paramref name="expectedVersion"/> (null: no row yet).</summary>
    Task<PauseWrite> WriteAsync(PauseState next, string? expectedVersion, CancellationToken cancellationToken);
}
