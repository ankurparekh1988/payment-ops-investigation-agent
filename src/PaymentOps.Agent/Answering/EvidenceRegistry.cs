using PaymentOps.Agent.Contracts;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// This turn's evidence. S1…Sn mean nothing to Search or the corpus; they exist for one model call and
/// its validation, and every citation shown is built from here, never from what the model wrote.
/// </summary>
internal sealed class EvidenceRegistry
{
    private readonly Dictionary<string, RetrievedSection> _byId;

    private EvidenceRegistry(IReadOnlyList<(string Id, RetrievedSection Section)> entries)
    {
        Entries = entries;
        _byId = entries.ToDictionary(e => e.Id, e => e.Section, StringComparer.Ordinal);
    }

    public IReadOnlyList<(string Id, RetrievedSection Section)> Entries { get; }

    public static EvidenceRegistry From(IReadOnlyList<RetrievedSection> sections) =>
        new([.. sections.Select((section, i) => ($"S{i + 1}", section))]);

    public bool Contains(string id) => _byId.ContainsKey(id);

    public Citation CitationFor(string id)
    {
        var section = _byId[id];
        return new Citation(id, section.Key, section.Title, section.SectionPath);
    }
}
