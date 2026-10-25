using Azure;
using Azure.Identity;

using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;

using static PaymentOps.Knowledge.Tests.TestSections;

namespace PaymentOps.Knowledge.Tests;

public sealed class DocumentsOnlyPublishTests
{
    private static KnowledgeIndexPublisher Publisher(FakeKnowledgeIndex index) => new(index, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Documents_only_never_creates_or_changes_the_index_definition()
    {
        var index = new FakeKnowledgeIndex().With();
        KnowledgeSearchDocument[] desired = [Section(h2: "A"), Section(h2: "B")];

        await Publisher(index).PublishDocumentsAsync(desired);

        Assert.DoesNotContain("ensure", index.Operations);
        Assert.Equal(desired.OrderBy(d => d.Id), index.Stored.OrderBy(d => d.Id));
    }

    [Fact]
    public async Task Documents_only_removes_what_an_earlier_evaluation_left()
    {
        var foreign = Section("runbook", h2: "Left by an earlier evaluation");
        var index = new FakeKnowledgeIndex().With(foreign);

        await Publisher(index).PublishDocumentsAsync([Section(h2: "A")]);

        Assert.DoesNotContain(index.Stored, d => d.Id == foreign.Id);
        Assert.Single(index.Stored);
    }

    [Fact]
    public async Task An_empty_corpus_is_refused_in_documents_only_mode_too()
    {
        var index = new FakeKnowledgeIndex().With(Section(h2: "A"));

        await Assert.ThrowsAsync<KnowledgePublishException>(() => Publisher(index).PublishDocumentsAsync([]));
        Assert.Single(index.Stored);
    }

    [Fact]
    public async Task A_publisher_that_can_write_the_live_index_is_refused()
    {
        var live = new FakeKnowledgeIndex().With();

        var error = await Assert.ThrowsAsync<PublisherScopeException>(() => PublisherScope.RequireNoWriteAsync(live, "knowledge-v1", CancellationToken.None));

        Assert.Contains("'knowledge-v1'", error.Message, StringComparison.Ordinal);
        Assert.Equal([$"delete:{PublisherScope.ProbeKey}"], live.Operations);
    }

    [Fact]
    public async Task A_403_from_the_live_index_is_the_expected_scope()
    {
        var live = new FakeKnowledgeIndex().With();
        live.DeleteFailure = SearchKnowledgeIndexClient.Explain(new RequestFailedException(403, "Forbidden"), "Deleting sections", "Search Index Data Contributor");

        await PublisherScope.RequireNoWriteAsync(live, "knowledge-v1", CancellationToken.None);
    }

    [Fact]
    public async Task A_scope_violation_is_its_own_exception()
    {
        var live = new FakeKnowledgeIndex().With();
        var unavailable = SearchKnowledgeIndexClient.Explain(new RequestFailedException(503, "Service unavailable"), "Deleting sections", "Search Index Data Contributor");
        live.DeleteFailure = unavailable;

        var error = await Assert.ThrowsAsync<KnowledgePublishException>(() => PublisherScope.RequireNoWriteAsync(live, "knowledge-v1", CancellationToken.None));

        Assert.Same(unavailable, error);
        Assert.False(typeof(KnowledgePublishException).IsAssignableFrom(typeof(PublisherScopeException)));
    }

    [Fact]
    public void Publish_exit_codes_name_the_failure_class()
    {
        Assert.Equal(0, PublishExitCode.For(null));
        Assert.Equal(4, PublishExitCode.For(new PublisherScopeException("knowledge-v1")));
        Assert.Equal(1, PublishExitCode.For(new KnowledgePublishException("The index doesn't match the corpus after publishing.")));
        Assert.Equal(1, PublishExitCode.For(new KnowledgeCorpusException(["bad"])));
        Assert.Equal(1, PublishExitCode.For(new AiReleaseException(["bad"])));
        Assert.Equal(3, PublishExitCode.For(new AuthenticationFailedException("no sign-in")));
        Assert.Equal(3, PublishExitCode.For(new HttpRequestException("no route")));
        Assert.Equal(3, PublishExitCode.For(SearchKnowledgeIndexClient.Explain(new RequestFailedException(403, "Forbidden"), "Uploading sections", "Search Index Data Contributor")));
        Assert.Equal(3, PublishExitCode.For(SearchKnowledgeIndexClient.Explain(new RequestFailedException(404, "Not found"), "Reading sections", "Search Index Data Contributor")));
        Assert.Equal(3, PublishExitCode.For(SearchKnowledgeIndexClient.Explain(new RequestFailedException(503, "Unavailable"), "Reading sections", "Search Index Data Contributor")));
        Assert.Equal(1, PublishExitCode.For(SearchKnowledgeIndexClient.Explain(new RequestFailedException(400, "Bad request"), "Uploading sections", "Search Index Data Contributor")));
        Assert.Equal(3, PublishExitCode.For(new AggregateException("Retry failed after 4 tries.", new HttpRequestException("no route"), new TaskCanceledException("timed out"))));
        Assert.Equal(1, PublishExitCode.For(new AggregateException(new InvalidOperationException("a bug"))));
    }
}
