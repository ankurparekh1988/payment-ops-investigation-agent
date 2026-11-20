using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace PaymentOps.Web.Security;

/// <summary>Whether the current user's session is live and authenticated recently enough for a sensitive action.</summary>
internal interface IRecentAuthentication
{
    bool IsFresh { get; }

    SessionStatus Status { get; }
}

internal sealed class RecentAuthenticationCheck(
    CurrentUserAccessor accessor, SessionRegistry sessions, TimeProvider time, IOptions<AccessOptions> access) : IRecentAuthentication
{
    public SessionStatus Status => sessions.Status(SessionClaims.SidOf(accessor.User), accessor.User.GetObjectId());

    // A dead session is never fresh, whatever time it last recorded.
    public bool IsFresh =>
        Status is { IsLive: true } status
        && RecentAuthentication.IsFresh(status.AuthenticatedAt, time.GetUtcNow(), access.Value.RecentAuthenticationWindow);
}
