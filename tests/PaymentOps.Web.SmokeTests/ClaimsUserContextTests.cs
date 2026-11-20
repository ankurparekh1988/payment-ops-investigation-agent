using System.Security.Claims;

using Microsoft.Extensions.Options;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests;

public sealed class ClaimsUserContextTests
{
    private const string Group = "8f6c1f0e-3a3b-4c47-9d8e-2b1f5f0c7a11";

    [Theory]
    [InlineData("", new[] { "" }, false)]
    [InlineData("", new[] { Group }, false)]
    [InlineData(Group, new[] { "another-group" }, false)]
    [InlineData(Group, new[] { Group }, true)]
    public void Restricted_knowledge_needs_a_configured_group_and_membership(string configuredGroup, string[] userGroups, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(userGroups.Select(g => new Claim("groups", g)).Append(new Claim("oid", "user-1")), "test"));
        var access = new AccessOptions { RestrictedGroupId = configuredGroup };
        var context = new ClaimsUserContext(new CurrentUserAccessor { User = user }, Options.Create(access), TestSessions.LiveFor(user, access));

        Assert.Equal(expected, context.CanAccessRestrictedKnowledge);
    }
}
