namespace PaymentOps.Knowledge.Corpus;

/// <summary>A validated source document: its metadata, where it came from, and the content to upload.</summary>
/// <param name="Classification">Taken from the folder the file is in, never from the file itself.</param>
/// <param name="Content">The Markdown body without the front matter, with LF line endings and one
/// final newline. This exact text is what gets indexed.</param>
/// <param name="ContentSha256">Hash of <paramref name="Content"/>. Metadata isn't part of it, so a
/// changed classification or doc type has to be detected separately.</param>
public sealed record KnowledgeSource(
    string DocId,
    string Title,
    string DocType,
    string Classification,
    string SourcePath,
    string Content,
    string ContentSha256,
    IReadOnlyList<SectionPath> SectionPaths);

/// <summary>
/// A heading path within a document: levels the path doesn't reach are empty. With the doc id it
/// anchors a citation, and it stays stable only as long as the headings do.
/// </summary>
public sealed record SectionPath(string H1, string H2, string H3)
{
    /// <summary>The headings it reaches, joined with " > ".</summary>
    public override string ToString() => string.Join(" > ", new[] { H1, H2, H3 }.Where(h => h.Length > 0));
}

public static class KnowledgeClassification
{
    public const string General = "general";
    public const string Restricted = "restricted";

    public static IReadOnlyList<string> All { get; } = [General, Restricted];
}

public static class KnowledgeDocType
{
    public static IReadOnlyList<string> All { get; } = ["runbook", "processor-doc", "policy", "incident-report"];
}

/// <summary>Project caps that catch a mistake early. They are well below the Search service limits.</summary>
public static class CorpusLimits
{
    public const int MaxDocuments = 60;
    public const int MaxContentBytes = 256 * 1024;
}
