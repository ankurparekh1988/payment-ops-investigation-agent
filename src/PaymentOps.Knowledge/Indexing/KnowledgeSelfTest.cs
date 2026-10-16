using System.Text;

using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// Proves the publishing behaviours against a throwaway index, never the live one: two small corpora,
/// A then B, where B renames and removes a heading and moves a document to restricted.
/// </summary>
public static class KnowledgeSelfTest
{
    public const string IndexName = "knowledge-selftest";

    private const string RunbookA = """
        ---
        docId: selftest-runbook
        title: Self-test runbook
        docType: runbook
        ---

        # Self-test runbook

        Overview.

        ## Steps

        ### Partial files

        Wait for the second file.

        ### Escalate

        Page the on-call engineer.
        """;

    private const string RunbookB = """
        ---
        docId: selftest-runbook
        title: Self-test runbook
        docType: runbook
        ---

        # Self-test runbook

        Overview.

        ## Steps

        ### Split files

        Wait for the second file.
        """;

    private const string Policy = """
        ---
        docId: selftest-policy
        title: Self-test policy
        docType: policy
        ---

        # Self-test policy

        ## Scope

        Applies to every hold.
        """;

    /// <summary>The self-test deletes its index, so it must never share a name with the live one.</summary>
    public static void RequireDistinctFrom(string liveIndexName)
    {
        if (liveIndexName == IndexName)
        {
            throw new KnowledgePublishException($"retrieval.index can't be '{IndexName}'; the self-test deletes that index.");
        }
    }

    public static async Task<IReadOnlyList<string>> RunAsync(
        IKnowledgeIndexClient index, string liveIndexName, Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        RequireDistinctFrom(liveIndexName);

        var recorder = new RecordingIndex(index);
        var publisher = new KnowledgeIndexPublisher(recorder, delay);
        var proved = new List<string>();

        await index.DeleteIndexAsync(cancellationToken);
        try
        {
            var a = Corpus((RunbookA, "selftest-runbook", KnowledgeClassification.General), (Policy, "selftest-policy", KnowledgeClassification.General));
            await publisher.PublishAsync(a, cancellationToken);
            var storedA = await index.ReadAllAsync(cancellationToken);

            Require(
                Paths(storedA).SequenceEqual(["Self-test policy > Scope", "Self-test runbook", "Self-test runbook > Steps > Escalate", "Self-test runbook > Steps > Partial files"]),
                "h1–h3 headings map to sections; empty headings aren't indexed", storedA);
            proved.Add($"h1–h3 headings map to sections: {storedA.Count} sections, the empty 'Steps' heading not indexed.");

            var metadata = a.ToDictionary(d => d.Id, d => (d.DocId, d.Title, d.DocType, d.Classification), StringComparer.Ordinal);
            Require(
                storedA.All(d => metadata.TryGetValue(d.Id, out var m) && m == (d.DocId, d.Title, d.DocType, d.Classification)),
                "every section carries its document's metadata", storedA);
            proved.Add("Every section carries its document's doc id, title, doc type and classification.");

            var b = Corpus((RunbookB, "selftest-runbook", KnowledgeClassification.General), (Policy, "selftest-policy", KnowledgeClassification.Restricted));
            recorder.Calls.Clear();
            await publisher.PublishAsync(b, cancellationToken);
            var storedB = await index.ReadAllAsync(cancellationToken);

            var policyKeys = string.Join(",", a.Where(d => d.DocId == "selftest-policy").Select(d => d.Id).Order(StringComparer.Ordinal));
            Require(
                recorder.Calls.Count >= 3 && recorder.Calls[0] == $"delete:{policyKeys}" && recorder.Calls[1] == $"find:{policyKeys}"
                    && recorder.Calls[2].StartsWith("upload:", StringComparison.Ordinal)
                    && storedB.Where(d => d.DocId == "selftest-policy").All(d => d.Classification == KnowledgeClassification.Restricted),
                "general to restricted deletes and confirms before uploading", storedB);
            proved.Add($"General to restricted: deleted, confirmed gone, then uploaded as restricted ({string.Join(" → ", recorder.Calls.Select(w => w.Split(':')[0]))}).");

            Require(
                Paths(storedB).SequenceEqual(["Self-test policy > Scope", "Self-test runbook", "Self-test runbook > Steps > Split files"]),
                "a renamed or removed heading leaves no stale section", storedB);
            proved.Add("A renamed heading and a removed heading leave no stale section.");

            return proved;
        }
        finally
        {
            await index.DeleteIndexAsync(CancellationToken.None);
        }
    }

    // Read with the same rules as a source file, at the path the file would have.
    private static List<KnowledgeSearchDocument> Corpus(params (string Text, string DocId, string Classification)[] sources)
    {
        var errors = new List<string>();
        var read = sources
            .Select(s => KnowledgeSourceReader.ReadSource(Encoding.UTF8.GetBytes(s.Text), $"{s.Classification}/{s.DocId}.md", s.Classification, errors))
            .ToList();

        if (errors.Count > 0 || read.Any(s => s is null))
        {
            throw new KnowledgePublishException("The self-test corpus is invalid: " + string.Join("; ", errors));
        }

        return [.. IndexedCorpus.FromSources(read.OfType<KnowledgeSource>())];
    }

    private static IEnumerable<string> Paths(IEnumerable<KnowledgeSearchDocument> documents) =>
        documents.Select(d => d.SectionPath).Order(StringComparer.Ordinal);

    private static void Require(bool condition, string behaviour, IReadOnlyList<KnowledgeSearchDocument> stored)
    {
        if (!condition)
        {
            throw new KnowledgePublishException($"Self-test failed: {behaviour}. The index held: {string.Join("; ", stored.Select(d => d.Describe()))}.");
        }
    }

    // Records the calls whose order matters, as they reach the index, for the tighten-order check.
    private sealed class RecordingIndex(IKnowledgeIndexClient inner) : IKnowledgeIndexClient
    {
        public List<string> Calls { get; } = [];

        public Task EnsureIndexAsync(CancellationToken cancellationToken) => inner.EnsureIndexAsync(cancellationToken);

        public Task DeleteIndexAsync(CancellationToken cancellationToken) => inner.DeleteIndexAsync(cancellationToken);

        public Task<IReadOnlyList<KnowledgeSearchDocument>> ReadAllAsync(CancellationToken cancellationToken) => inner.ReadAllAsync(cancellationToken);

        public Task<IReadOnlyList<string>> FindExistingAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            Calls.Add("find:" + Join(keys));
            return inner.FindExistingAsync(keys, cancellationToken);
        }

        public Task UploadAsync(IReadOnlyCollection<KnowledgeSearchDocument> documents, CancellationToken cancellationToken)
        {
            Calls.Add("upload:" + Join(documents.Select(d => d.Id)));
            return inner.UploadAsync(documents, cancellationToken);
        }

        public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            Calls.Add("delete:" + Join(keys));
            return inner.DeleteAsync(keys, cancellationToken);
        }

        private static string Join(IEnumerable<string> keys) => string.Join(",", keys.Order(StringComparer.Ordinal));
    }
}
