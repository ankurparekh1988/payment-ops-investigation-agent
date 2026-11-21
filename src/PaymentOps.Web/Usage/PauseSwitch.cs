using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.Usage;

/// <summary>
/// Pauses or resumes investigations for everyone, for Admins; resuming also needs a recent sign-in. Both are checked
/// here on every call, like an approval, so hiding the button is never the control. Pausing doesn't need one: it only
/// stops new investigations, and an Admin in an incident shouldn't have to sign in again to do it. The change is decided from a fresh read, so
/// asking for the state it's already in reports it as it is; each change is logged with a pseudonymous actor and the
/// reason's length.
/// </summary>
internal sealed partial class PauseSwitch(
    IPauseStore store, IUserContext user, IRecentAuthentication recent, TimeProvider time, ILogger<PauseSwitch> logger) : IPauseSwitch
{
    public const int MaxReasonLength = 200;

    public async Task<PauseResult> SetAsync(bool paused, string? reason, CancellationToken cancellationToken)
    {
        if (Refusal(paused) is { } refused)
        {
            return refused;
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

        // Again, with nothing awaited before the write: the session can end or go stale while the read is pending.
        if (Refusal(paused) is { } late)
        {
            return late;
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

    // A signed-in Admin first, then a recent sign-in to resume; neither reads the store.
    private PauseResult? Refusal(bool pausing) =>
        !user.IsAuthenticated || !user.IsInRole(OpsRoles.Admin) ? new PauseResult.NotAuthorized()
        : !pausing && !recent.IsFresh ? new PauseResult.ReauthenticationRequired()
        : null;

    // Stored and shown as plain text; only its length is logged.
    private static string? Clean(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, MaxReasonLength)];
    }

    [LoggerMessage(EventId = 3301, Level = LogLevel.Warning, Message = "Investigation pause set to {Paused} by {Actor} (reason: {ReasonLength} characters)")]
    private static partial void Changed(ILogger logger, bool paused, string actor, int reasonLength);
}
