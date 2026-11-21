using Microsoft.Identity.Web;
using Microsoft.JSInterop;

namespace PaymentOps.Web.Security;

/// <summary>Counts the person's own input in the page as activity. Circuit traffic alone never does.</summary>
internal sealed class ActivityReporter(CurrentUserAccessor accessor, SessionRegistry sessions)
{
    /// <summary>Renews the session if it is still live, and says whether it is.</summary>
    [JSInvokable]
    public bool ReportActivity()
    {
        var user = accessor.User;
        return SessionClaims.SidOf(user) is { } sid && user.GetObjectId() is { } userId && sessions.Touch(sid, userId);
    }
}
