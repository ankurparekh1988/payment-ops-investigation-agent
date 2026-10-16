using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;

using static PaymentOps.Knowledge.Tests.TestSections;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeIndexPublisherTests
{
    private static KnowledgeIndexPublisher Publisher(FakeKnowledgeIndex index) => new(index, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Publishes_into_an_empty_index_and_verifies_it()
    {
        var index = new FakeKnowledgeIndex();
        KnowledgeSearchDocument[] desired = [Section(h2: "A"), Section(h2: "B")];

        await Publisher(index).PublishAsync(desired);

        Assert.Equal(desired.OrderBy(d => d.Id), index.Stored.OrderBy(d => d.Id));
        Assert.Equal("ensure", index.Operations[0]);
        Assert.Equal("read", index.Operations[^1]);
    }

    [Fact]
    public async Task Tightens_before_uploading_and_deletes_stale_sections_last()
    {
        var general = Section("policy", h2: "Scope");
        var stale = Section("runbook", h2: "Old");
        var index = new FakeKnowledgeIndex().With(general, stale);
        var restricted = general with { Classification = KnowledgeClassification.Restricted };

        await Publisher(index).PublishAsync([restricted, Section("runbook", h2: "New")]);

        var writes = index.Operations.Where(o => o.Split(':')[0] is "delete" or "find" or "upload").ToList();
        Assert.Equal(
            [$"delete:{general.Id}", $"find:{general.Id}", "upload:" + string.Join(",", new[] { restricted.Id, Section("runbook", h2: "New").Id }.Order(StringComparer.Ordinal)), $"delete:{stale.Id}"],
            writes);
    }

    [Fact]
    public async Task Stops_before_uploading_if_a_tightened_section_is_still_there()
    {
        var general = Section("policy", h2: "Scope");
        var index = new FakeKnowledgeIndex().With(general);
        index.StuckKeys.Add(general.Id);

        var error = await Assert.ThrowsAsync<KnowledgePublishException>(() =>
            Publisher(index).PublishAsync([general with { Classification = KnowledgeClassification.Restricted }]));

        Assert.Contains("policy: Title of policy > Scope", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(index.Operations, o => o.StartsWith("upload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verification_fails_when_a_stored_field_differs()
    {
        var index = new FakeKnowledgeIndex { OnRead = d => d with { Classification = "general-ish" } };

        var error = await Assert.ThrowsAsync<KnowledgePublishException>(() => Publisher(index).PublishAsync([Section()]));

        Assert.Contains("differs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_waits_for_the_index_to_catch_up()
    {
        var index = new FakeKnowledgeIndex();

        await Publisher(index).PublishAsync([Section()]);
        index.LaggingReads = 3;
        await Publisher(index).PublishAsync([Section(content: "Changed.")]);

        Assert.Equal("Changed.", Assert.Single(index.Stored).Content);
    }

    [Fact]
    public async Task Makes_no_writes_when_nothing_changed()
    {
        var index = new FakeKnowledgeIndex().With(Section());

        var plan = await Publisher(index).PublishAsync([Section()]);

        Assert.True(plan.IsEmpty);
        Assert.Equal(["ensure", "read", "read"], index.Operations);
    }

    [Fact]
    public async Task Refuses_an_empty_corpus()
    {
        await Assert.ThrowsAsync<KnowledgePublishException>(() => Publisher(new FakeKnowledgeIndex()).PublishAsync([]));
    }
}
