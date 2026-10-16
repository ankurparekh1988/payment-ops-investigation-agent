using System.Text.Json;

using Azure;

using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Tests;

public sealed class SearchKnowledgeIndexClientTests
{
    [Fact]
    public void A_forbidden_call_names_the_role_and_the_bootstrap()
    {
        var error = SearchKnowledgeIndexClient.Explain(new RequestFailedException(403, "Forbidden"), "Uploading sections", "Search Index Data Contributor");

        Assert.Contains("Search Index Data Contributor", error.Message, StringComparison.Ordinal);
        Assert.Contains("scripts/bootstrap.sh", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unauthenticated_call_points_at_the_token_not_the_role()
    {
        var error = SearchKnowledgeIndexClient.Explain(new RequestFailedException(401, "Unauthorized"), "Uploading sections", "Search Index Data Contributor");

        Assert.Contains("token", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_the_index_cant_take_says_to_change_the_index_name()
    {
        var error = SearchKnowledgeIndexClient.Explain(
            new RequestFailedException(400, "Bad request", "OperationNotAllowed", null), "Updating index 'knowledge-v1'", "Search Service Contributor",
            schemaHint: "change retrieval.index in ai/manifest.yaml");

        Assert.Contains("retrieval.index", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_bad_requests_dont_get_the_schema_hint()
    {
        var error = SearchKnowledgeIndexClient.Explain(
            new RequestFailedException(400, "Bad request", "InvalidRequestParameter", null), "Updating index 'knowledge-v1'", "Search Service Contributor",
            schemaHint: "change retrieval.index in ai/manifest.yaml");

        Assert.DoesNotContain("retrieval.index", error.Message, StringComparison.Ordinal);
        Assert.Contains("InvalidRequestParameter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_every_page_however_many_documents_are_stored()
    {
        var stored = Enumerable.Range(0, 2500).Select(i => TestSections.Section(docId: $"doc-{i:D4}") with { Id = $"doc-{i:D4}--k" }).ToList();
        var afters = new List<string?>();

        var read = await SearchKnowledgeIndexClient.ReadAllPagesAsync((after, _) =>
        {
            afters.Add(after);
            IReadOnlyList<KnowledgeSearchDocument> page = [.. stored.Where(d => after is null || string.CompareOrdinal(d.Id, after) > 0).Take(1000)];
            return Task.FromResult(page);
        }, CancellationToken.None);

        Assert.Equal(stored.Select(d => d.Id), read.Select(d => d.Id));
        Assert.Equal([null, "doc-0999--k", "doc-1999--k"], afters);
    }

    [Fact]
    public void Upload_batches_stop_at_1000_sections()
    {
        var documents = Enumerable.Range(0, 2500).Select(i => TestSections.Section(docId: $"doc-{i}"));

        Assert.Equal([1000, 1000, 500], SearchKnowledgeIndexClient.UploadBatches(documents).Select(b => b.Length));
    }

    [Fact]
    public void Upload_batches_stop_before_the_request_size_limit()
    {
        var documents = Enumerable.Range(0, 5).Select(i => TestSections.Section(docId: $"doc-{i}", content: new string('x', 400))).ToList();
        var size = JsonSerializer.SerializeToUtf8Bytes(documents[0], KnowledgeIndexSchema.Json).Length;

        Assert.Equal([2, 2, 1], SearchKnowledgeIndexClient.UploadBatches(documents, maxBytes: size * 5 / 2).Select(b => b.Length));
    }

    [Fact]
    public void A_section_too_large_for_one_request_is_refused_by_name()
    {
        var documents = new[] { TestSections.Section(docId: "large", h2: "Steps", content: new string('x', 2000)) };

        var error = Assert.Throws<KnowledgePublishException>(() => SearchKnowledgeIndexClient.UploadBatches(documents, maxBytes: 1000).ToList());

        Assert.Contains("large: Title of large > Steps", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_failures_keep_the_status_and_error_code()
    {
        var error = SearchKnowledgeIndexClient.Explain(new RequestFailedException(503, "Unavailable", "ServiceUnavailable", null), "Reading the index", "Search Index Data Contributor");

        Assert.Contains("503", error.Message, StringComparison.Ordinal);
        Assert.Contains("ServiceUnavailable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_heading_fields_read_back_as_null_compare_as_empty()
    {
        var stored = JsonSerializer.Deserialize<KnowledgeSearchDocument>(
            """{"id":"k","doc_id":"d","title":"T","doc_type":"runbook","classification":"general","h2":null,"h3":null,"section_path":"T","ordinal":0,"content":"x"}""",
            KnowledgeIndexSchema.Json)!;

        Assert.Equal(new KnowledgeSearchDocument("k", "d", "T", "runbook", "general", "", "", "T", 0, "x"), SearchKnowledgeIndexClient.Normalize(stored));
    }
}
