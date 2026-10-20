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
        var accessor = new CurrentUserAccessor
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(userGroups.Select(g => new Claim("groups", g)), "test")),
        };
        var context = new ClaimsUserContext(accessor, Options.Create(new AccessOptions { RestrictedGroupId = configuredGroup }));

        Assert.Equal(expected, context.CanAccessRestrictedKnowledge);
    }
}
