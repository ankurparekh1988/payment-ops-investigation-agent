using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// What turns the stored index into the desired one. Sections of a document that is now restricted
/// but stored as anything else are tightened: deleted and confirmed gone before any upload, so they
/// are briefly missing rather than briefly visible as general. Everything else uploads first and
/// deletes afterwards, so an ordinary edit never leaves a gap.
/// </summary>
/// <param name="Tighten">Stored documents to delete first.</param>
/// <param name="Upload">Desired documents that are missing or differ in any field.</param>
/// <param name="Delete">Stored documents no longer in the corpus, deleted last.</param>
public sealed record PublishPlan(
    IReadOnlyList<KnowledgeSearchDocument> Tighten,
    IReadOnlyList<KnowledgeSearchDocument> Upload,
    IReadOnlyList<KnowledgeSearchDocument> Delete)
{
    public bool IsEmpty => Tighten.Count == 0 && Upload.Count == 0 && Delete.Count == 0;

    public static PublishPlan Create(IReadOnlyList<KnowledgeSearchDocument> desired, IReadOnlyList<KnowledgeSearchDocument> existing)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(existing);

        var wanted = desired.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var stored = existing.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var restrictedDocs = desired.Where(d => d.Classification == KnowledgeClassification.Restricted)
            .Select(d => d.DocId).ToHashSet(StringComparer.Ordinal);

        var tighten = existing
            .Where(e => e.Classification != KnowledgeClassification.Restricted
                && (restrictedDocs.Contains(e.DocId) || (wanted.TryGetValue(e.Id, out var w) && w.Classification == KnowledgeClassification.Restricted)))
            .ToList();
        var tightened = tighten.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        // Record equality compares every field, so a metadata-only change is an upload too.
        var upload = desired.Where(d => !stored.TryGetValue(d.Id, out var s) || s != d).ToList();
        var delete = existing.Where(e => !wanted.ContainsKey(e.Id) && !tightened.Contains(e.Id)).ToList();

        return new PublishPlan(ById(tighten), ById(upload), ById(delete));
    }

    /// <summary>One line per change, naming the section but never including its text.</summary>
    public IEnumerable<string> Changes() =>
        Tighten.Select(d => $"tighten  {d.Describe()}")
            .Concat(Upload.Select(d => $"upload   {d.Describe()}"))
            .Concat(Delete.Select(d => $"delete   {d.Describe()}"));

    private static List<KnowledgeSearchDocument> ById(IEnumerable<KnowledgeSearchDocument> documents) =>
        [.. documents.OrderBy(d => d.Id, StringComparer.Ordinal)];
}
