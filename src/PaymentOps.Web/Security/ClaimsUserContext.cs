using System.Security.Claims;

using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

/// <summary>
/// Reads the signed-in user's identity, roles and groups from their Entra ID token claims, but only while
/// their session is live: an expired, revoked or unknown session reads as signed out.
/// </summary>
internal sealed class ClaimsUserContext(CurrentUserAccessor accessor, IOptions<AccessOptions> access, SessionRegistry sessions) : IUserContext
{
    private const string NameClaim = "name";
    private const string GroupsClaim = "groups";

    // Derived once per principal, and again whenever the user seen changes, including to signed out when the session ends.
    private ClaimsPrincipal? _derivedFrom;
    private IReadOnlySet<string> _roles = new HashSet<string>();
    private IReadOnlySet<string> _groupIds = new HashSet<string>();

    private static readonly ClaimsPrincipal SignedOut = new(new ClaimsIdentity());

    // Checked on every call, so a context held by an open circuit signs out the moment the session ends.
    private ClaimsPrincipal User =>
        accessor.User is var user && sessions.Status(SessionClaims.SidOf(user), user.GetObjectId()).IsLive ? user : SignedOut;

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public string UserId => User.GetObjectId() ?? string.Empty;

    public string DisplayName => User.FindFirst(NameClaim)?.Value ?? User.Identity?.Name ?? string.Empty;

    public IReadOnlySet<string> Roles
    {
        get
        {
            Derive();
            return _roles;
        }
    }

    public IReadOnlySet<string> GroupIds
    {
        get
        {
            Derive();
            return _groupIds;
        }
    }

    // Fails closed: with no group configured, nobody gets Restricted knowledge, even with an empty groups claim.
    public bool CanAccessRestrictedKnowledge =>
        !string.IsNullOrEmpty(access.Value.RestrictedGroupId) && GroupIds.Contains(access.Value.RestrictedGroupId);

    public bool IsInRole(string role) => Roles.Contains(role);

    private void Derive()
    {
        var user = User;
        if (ReferenceEquals(_derivedFrom, user))
        {
            return;
        }

        _roles = OpsRoles.Effective(user.Identities.SelectMany(identity => identity.FindAll(identity.RoleClaimType)).Select(claim => claim.Value));
        _groupIds = user.FindAll(GroupsClaim).Select(claim => claim.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _derivedFrom = user;
    }
}
