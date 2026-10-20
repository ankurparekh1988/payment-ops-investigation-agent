using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeAccessTests
{
    [Fact]
    public void An_unauthenticated_user_gets_no_access()
    {
        Assert.Throws<KnowledgeAccessDeniedException>(() => KnowledgeAccess.From(TestUser.Anonymous));
    }

    [Fact]
    public void A_user_without_the_restricted_capability_sees_general_only()
    {
        var access = KnowledgeAccess.From(TestUser.General);

        Assert.False(access.IncludesRestricted);
        Assert.Equal("classification eq 'general'", access.Predicate);
    }

    [Fact]
    public void A_user_with_the_restricted_capability_sees_both()
    {
        var access = KnowledgeAccess.From(TestUser.Restricted);

        Assert.True(access.IncludesRestricted);
        Assert.Equal("(classification eq 'general' or classification eq 'restricted')", access.Predicate);
    }
}
