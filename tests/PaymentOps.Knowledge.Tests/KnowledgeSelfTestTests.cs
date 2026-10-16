using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeSelfTestTests
{
    private static Task<IReadOnlyList<string>> Run(FakeKnowledgeIndex index) =>
        KnowledgeSelfTest.RunAsync(index, "knowledge-v1", (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Refuses_before_touching_any_index_when_the_live_index_has_its_name()
    {
        var index = new FakeKnowledgeIndex();

        await Assert.ThrowsAsync<KnowledgePublishException>(() => KnowledgeSelfTest.RunAsync(index, KnowledgeSelfTest.IndexName, (_, _) => Task.CompletedTask));

        Assert.Empty(index.Operations);
    }

    [Fact]
    public async Task Proves_the_four_behaviours_and_removes_its_index()
    {
        var index = new FakeKnowledgeIndex();

        var proved = await Run(index);

        Assert.Equal(4, proved.Count);
        Assert.False(index.Exists);
        Assert.Equal("delete-index", index.Operations[0]);
        Assert.Equal("delete-index", index.Operations[^1]);
    }

    [Fact]
    public async Task A_tighten_that_doesnt_take_fails_and_still_removes_the_index()
    {
        var index = new FakeKnowledgeIndex();
        index.StuckKeys.Add(SectionKey.For("selftest-policy", new SectionPath("Self-test policy", "Scope", "")));

        await Assert.ThrowsAsync<KnowledgePublishException>(() => Run(index));

        Assert.False(index.Exists);
    }

    [Fact]
    public async Task A_wrong_stored_field_fails_the_metadata_check()
    {
        var index = new FakeKnowledgeIndex { OnRead = d => d with { DocType = "incident-report" } };

        await Assert.ThrowsAsync<KnowledgePublishException>(() => Run(index));
    }

    [Fact]
    public async Task Passes_when_the_first_lookups_after_the_tighten_still_find_the_section()
    {
        var index = new FakeKnowledgeIndex { LaggingFinds = 2 };

        var proved = await Run(index);

        Assert.Equal(4, proved.Count);
    }

    [Fact]
    public async Task A_failed_cleanup_doesnt_hide_why_the_self_test_failed()
    {
        var index = new FakeKnowledgeIndex { FailDeleteOfCreatedIndex = true };
        index.StuckKeys.Add(SectionKey.For("selftest-policy", new SectionPath("Self-test policy", "Scope", "")));

        var error = await Assert.ThrowsAsync<KnowledgePublishException>(() => Run(index));

        Assert.Contains("still retrievable", error.Message, StringComparison.Ordinal);
    }
}
