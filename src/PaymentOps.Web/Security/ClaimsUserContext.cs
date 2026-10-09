using System.Security.Claims;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

/// <summary>Reads the signed-in user's identity, roles and groups from their Entra ID token claims.</summary>
internal sealed class ClaimsUserContext(CurrentUserAccessor accessor) : IUserContext
{
    private const string ObjectIdClaim = "oid";
    private const string ObjectIdClaimUri = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string NameClaim = "name";
    private const string GroupsClaim = "groups";

    private ClaimsPrincipal User => accessor.User;

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public string UserId => (User.FindFirst(ObjectIdClaim) ?? User.FindFirst(ObjectIdClaimUri))?.Value ?? string.Empty;

    public string DisplayName => User.FindFirst(NameClaim)?.Value ?? User.Identity?.Name ?? string.Empty;

    public IReadOnlySet<string> Roles =>
        OpsRoles.Effective(User.Identities.SelectMany(identity => identity.FindAll(identity.RoleClaimType)).Select(claim => claim.Value));

    public IReadOnlySet<string> GroupIds =>
        User.FindAll(GroupsClaim).Select(claim => claim.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsInRole(string role) => Roles.Contains(role);
}
