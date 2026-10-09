using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests;

public sealed class AuthorizationTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
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
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("oid", "3f2a"),
                new Claim("name", "Casey Compliance"),
                new Claim("roles", OpsRoles.Engineer),
                new Claim("groups", "group-a"),
                new Claim("groups", "group-b"),
            ],
            authenticationType: "Test",
            nameType: "name",
            roleType: "roles"));

        var user = new ClaimsUserContext(new CurrentUserAccessor { User = principal });

        Assert.True(user.IsAuthenticated);
        Assert.Equal("3f2a", user.UserId);
        Assert.Equal("Casey Compliance", user.DisplayName);
        Assert.True(user.IsInRole(OpsRoles.Reader));
        Assert.False(user.IsInRole(OpsRoles.Admin));
        Assert.Equal(["group-a", "group-b"], user.GroupIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_anonymous_user_has_no_roles_or_groups()
    {
        var user = new ClaimsUserContext(new CurrentUserAccessor());

        Assert.False(user.IsAuthenticated);
        Assert.Empty(user.Roles);
        Assert.Empty(user.GroupIds);
    }

    private async Task<AuthorizationResult> Authorize(string policy, string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("roles", role)], authenticationType: "Test", nameType: "name", roleType: "roles"));

        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return await authorization.AuthorizeAsync(principal, policy);
    }
}
