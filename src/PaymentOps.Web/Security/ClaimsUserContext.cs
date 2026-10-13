using System.Security.Claims;

using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

/// <summary>Reads the signed-in user's identity, roles and groups from their Entra ID token claims.</summary>
internal sealed class ClaimsUserContext(CurrentUserAccessor accessor, IOptions<AccessOptions> access) : IUserContext
{
    private const string NameClaim = "name";
    private const string GroupsClaim = "groups";

    // Derived once per signed-in user; the accessor's user changes only when they sign in or out.
    private ClaimsPrincipal? _derivedFrom;
    private IReadOnlySet<string> _roles = new HashSet<string>();
    private IReadOnlySet<string> _groupIds = new HashSet<string>();

    private ClaimsPrincipal User => accessor.User;

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

    public bool CanAccessRestrictedKnowledge => GroupIds.Contains(access.Value.RestrictedGroupId);

    public bool IsInRole(string role) => Roles.Contains(role);

    private void Derive()
    {
        if (ReferenceEquals(_derivedFrom, User))
        {
            return;
        }

        _roles = OpsRoles.Effective(User.Identities.SelectMany(identity => identity.FindAll(identity.RoleClaimType)).Select(claim => claim.Value));
        _groupIds = User.FindAll(GroupsClaim).Select(claim => claim.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _derivedFrom = User;
    }
}
