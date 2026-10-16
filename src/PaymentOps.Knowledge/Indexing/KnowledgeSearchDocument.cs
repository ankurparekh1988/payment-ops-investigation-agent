namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// A section as stored in the search index. Every field is retrievable and compared, so a changed
/// classification or a manual edit is seen as a difference, not trusted from a stored fingerprint.
/// </summary>
public sealed record KnowledgeSearchDocument(
    string Id,
    string DocId,
    string Title,
    string DocType,
    string Classification,
    string H2,
    string H3,
    string SectionPath,
    int Ordinal,
    string Content)
{
    /// <summary>For logs: identifies the section without its text.</summary>
    public string Describe() => $"{DocId}: {SectionPath}";
}
