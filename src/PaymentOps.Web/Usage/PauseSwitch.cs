using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Usage;

/// <summary>
/// Pauses or resumes investigations for everyone, for Admins only: the role is checked here on every call, like an
/// approval, so hiding the button is never the control. The change is decided from a fresh read, so asking for the
/// state it's already in reports it as it is; each change is logged with a pseudonymous actor and the reason's length.
/// </summary>
internal sealed partial class PauseSwitch(IPauseStore store, IUserContext user, TimeProvider time, ILogger<PauseSwitch> logger) : IPauseSwitch
{
    public const int MaxReasonLength = 200;

    public Task<PauseState> GetAsync(CancellationToken cancellationToken) => store.ReadAsync(cancellationToken);

    public async Task<PauseResult> SetAsync(bool paused, string? reason, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || !user.IsInRole(OpsRoles.Admin))
        {
            return new PauseResult.NotAuthorized();
        }

        PauseState current;
        try
        {
            current = await store.ReadFreshAsync(cancellationToken);
        }
        catch (PauseUnavailableException)
        {
            return new PauseResult.Unavailable();
        }

        if (current.Paused == paused)
        {
            return new PauseResult.Conflict(current);
        }

        var actor = TableUsageCounter.UserRow(user.UserId);
        var next = new PauseState(paused, actor, user.DisplayName, time.GetUtcNow(), paused ? Clean(reason) : null);
        switch (await store.WriteAsync(next, current.Version, cancellationToken))
        {
            case PauseWrite.Written written:
                Changed(logger, paused, actor, written.State.Reason?.Length ?? 0);
                return new PauseResult.Changed(written.State);
            case PauseWrite.Conflict conflict:
                return new PauseResult.Conflict(conflict.Current);
            default:
                return new PauseResult.Unavailable();
        }
    }

    // Stored and shown as plain text; only its length is logged.
    private static string? Clean(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, MaxReasonLength)];
    }

    [LoggerMessage(EventId = 3301, Level = LogLevel.Warning, Message = "Investigation pause set to {Paused} by {Actor} (reason: {ReasonLength} characters)")]
    private static partial void Changed(ILogger logger, bool paused, string actor, int reasonLength);
}
