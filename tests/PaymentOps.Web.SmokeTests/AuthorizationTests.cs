using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests;

public sealed class AuthorizationTests
{
    private const string RestrictedGroupId = "group-restricted";

    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorization(AuthorizationPolicies.Configure)
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    [Theory]
    [InlineData(OpsRoles.Reader, false)]
    [InlineData(OpsRoles.Engineer, true)]
    [InlineData(OpsRoles.Admin, true)]
    public async Task Only_engineers_and_admins_can_approve_actions(string role, bool allowed)
    {
        var result = await Authorize(AuthorizationPolicies.ApproveActions, role);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Theory]
    [InlineData(OpsRoles.Reader, false)]
    [InlineData(OpsRoles.Engineer, false)]
    [InlineData(OpsRoles.Admin, true)]
    public async Task Only_admins_can_administer(string role, bool allowed)
    {
        var result = await Authorize(AuthorizationPolicies.Administer, role);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public void Higher_roles_include_lower_ones()
    {
        Assert.Equal([OpsRoles.Reader], OpsRoles.Effective([OpsRoles.Reader]).Order(StringComparer.Ordinal));
        Assert.Equal([OpsRoles.Engineer, OpsRoles.Reader], OpsRoles.Effective([OpsRoles.Engineer]).Order(StringComparer.Ordinal));
        Assert.Equal(3, OpsRoles.Effective([OpsRoles.Admin]).Count);
        Assert.Empty(OpsRoles.Effective(["Some.Other.Role"]));
    }

    [Fact]
    public void User_context_reads_identity_roles_and_groups_from_entra_claims()
    {
        var principal = TestPrincipal.From(
        [
            new Claim("oid", "3f2a"),
            new Claim("name", "Casey Compliance"),
            new Claim("roles", OpsRoles.Engineer),
            new Claim("groups", "group-a"),
            new Claim("groups", RestrictedGroupId),
        ]);

        var user = UserContextFor(principal);

        Assert.True(user.IsAuthenticated);
        Assert.Equal("3f2a", user.UserId);
        Assert.Equal("Casey Compliance", user.DisplayName);
        Assert.True(user.IsInRole(OpsRoles.Reader));
        Assert.False(user.IsInRole(OpsRoles.Admin));
        Assert.Equal(["group-a", RestrictedGroupId], user.GroupIds.Order(StringComparer.Ordinal));
        Assert.True(user.CanAccessRestrictedKnowledge);
    }

    [Fact]
    public void An_anonymous_user_has_no_roles_or_groups()
    {
        var user = UserContextFor(new ClaimsPrincipal());

        Assert.False(user.IsAuthenticated);
        Assert.Empty(user.Roles);
        Assert.Empty(user.GroupIds);
        Assert.False(user.CanAccessRestrictedKnowledge);
    }

    private static Task<AuthorizationResult> Authorize(string policy, string role) =>
        Authorization.AuthorizeAsync(TestPrincipal.From([new Claim("roles", role)]), policy);

    private static ClaimsUserContext UserContextFor(ClaimsPrincipal principal) =>
        new(new CurrentUserAccessor { User = principal }, Options.Create(new AccessOptions { RestrictedGroupId = RestrictedGroupId }));
}
