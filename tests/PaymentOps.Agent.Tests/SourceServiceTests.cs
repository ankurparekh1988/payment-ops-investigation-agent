using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Agent.Tests;

public sealed class SourceServiceTests
{
    private static readonly RetrievedSection General = Section("key-a", "general");
    private static readonly RetrievedSection Restricted = Section("key-r", "restricted");

    [Fact]
    public async Task A_source_the_user_may_see_is_found()
    {
        var found = Assert.IsType<SourceResult.Found>(await Service(Lookup()).GetSourceAsync("key-a", CancellationToken.None));

        Assert.Equal("Doc > One", found.SectionPath);
    }

    [Fact]
    public async Task A_restricted_source_is_not_found_for_a_general_user()
    {
        Assert.IsType<SourceResult.NotFound>(await Service(Lookup()).GetSourceAsync("key-r", CancellationToken.None));
        Assert.IsType<SourceResult.Found>(await Service(Lookup(), TestUser.Restricted).GetSourceAsync("key-r", CancellationToken.None));
    }

    [Fact]
    public async Task An_unauthenticated_source_request_is_not_found()
    {
        Assert.IsType<SourceResult.NotFound>(await Service(Lookup(), TestUser.Anonymous).GetSourceAsync("key-a", CancellationToken.None));
    }

    [Fact]
    public async Task Search_unavailable_for_a_source_is_unavailable()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([])) { Find = (_, _, _) => throw new KnowledgeUnavailableException("down") };

        Assert.IsType<SourceResult.Unavailable>(await Service(knowledge).GetSourceAsync("key-a", CancellationToken.None));
    }

    [Fact]
    public async Task A_source_lookup_that_outlasts_the_tool_timeout_is_unavailable()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([]))
        {
            Find = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            },
        };

        var result = await Service(knowledge, limits: new InvestigationLimits { ToolTimeout = TimeSpan.FromMilliseconds(50) }).GetSourceAsync("key-a", CancellationToken.None);

        Assert.IsType<SourceResult.Unavailable>(result);
    }

    private static RetrievedSection Section(string key, string classification) => new(key, "doc", "Doc", "Doc > One", "Text", classification, null);

    // Finds a section only when the caller's access covers it, like the real index filter.
    private static FakeKnowledgeLookup Lookup() => new((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([]))
    {
        Find = (key, access, _) => Task.FromResult(key == General.Key ? General : key == Restricted.Key && access.IncludesRestricted ? Restricted : null),
    };

    private static SourceService Service(FakeKnowledgeLookup knowledge, TestUser? user = null, InvestigationLimits? limits = null) =>
        new(knowledge, user ?? TestUser.General, Options.Create(limits ?? new InvestigationLimits()), NullLogger<SourceService>.Instance);
}
