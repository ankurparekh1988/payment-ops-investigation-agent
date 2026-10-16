using System.Text;
using System.Text.Json;

namespace PaymentOps.Knowledge.Corpus;

/// <summary>
/// What a corpus build produced, written as <c>corpus.json</c> beside the content files. Uploading
/// reads this rather than the source folders, so it only ever publishes what was built and checked.
/// </summary>
public sealed record CorpusManifest(int SchemaVersion, IReadOnlyList<CorpusDocument> Documents)
{
    public const string FileName = "corpus.json";

    /// <summary>The format this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public static CorpusManifest From(IEnumerable<KnowledgeSource> sources) =>
        new(CurrentSchemaVersion, [.. sources.Select(s => new CorpusDocument(
            s.DocId, s.Title, s.DocType, s.Classification, ContentPath(s), s.ContentSha256, s.SectionPaths))]);

    /// <summary>
    /// Writes each source's content and <c>corpus.json</c> into <paramref name="outputDirectory"/>,
    /// replacing a previous build there so a stale document can't be uploaded by mistake.
    /// </summary>
    public static CorpusManifest Write(IReadOnlyList<KnowledgeSource> sources, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(sources);

        PrepareOutput(outputDirectory);
        foreach (var source in sources)
        {
            File.WriteAllText(Path.Combine(outputDirectory, ContentPath(source)), source.Content, Utf8);
        }

        var manifest = From(sources);
        File.WriteAllText(Path.Combine(outputDirectory, FileName), manifest.ToJson(), Utf8);
        return manifest;
    }

    // Named by doc id alone, so moving a source between classification folders changes its
    // metadata, not its identity.
    private static string ContentPath(KnowledgeSource source) => $"{source.DocId}.md";

    /// <summary>The same corpus always serializes to the same bytes: documents are sorted by id.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this with { Documents = [.. Documents.OrderBy(d => d.DocId, StringComparer.Ordinal)] }, Json) + "\n";

    // A non-empty directory is reused only if it holds exactly the files its own corpus.json lists, and
    // only those files are deleted. Anything else is refused before a single file is touched.
    private static void PrepareOutput(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory) || !Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            Directory.CreateDirectory(outputDirectory);
            return;
        }

        var refused = new KnowledgeCorpusException([$"Output directory '{outputDirectory}' isn't empty and isn't exactly a previous corpus build; choose another."]);
        if (Directory.EnumerateDirectories(outputDirectory).Any() || PreviousBuildFiles(outputDirectory) is not { } generated)
        {
            throw refused;
        }

        var existing = Directory.EnumerateFiles(outputDirectory).Select(Path.GetFileName).ToList();
        if (existing.Any(name => !generated.Contains(name!)))
        {
            throw refused;
        }

        foreach (var name in existing)
        {
            File.Delete(Path.Combine(outputDirectory, name!));
        }
    }

    /// <summary>The corpus.json in <paramref name="directory"/>, or null if it isn't one this build reads.
    /// Every content path is checked to be a plain <c>&lt;docId&gt;.md</c> beside it.</summary>
    internal static CorpusManifest? TryRead(string directory)
    {
        CorpusManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(Path.Combine(directory, FileName)), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }

        return manifest is { SchemaVersion: CurrentSchemaVersion, Documents: { } documents }
            && documents.All(d => d?.DocId is not null && KnowledgeSourceReader.DocIdPattern().IsMatch(d.DocId) && d.ContentPath == $"{d.DocId}.md")
            ? manifest
            : null;
    }

    // The files a previous build wrote, according to its corpus.json, or null if there isn't one this build reads.
    private static HashSet<string>? PreviousBuildFiles(string outputDirectory) =>
        TryRead(outputDirectory) is { } previous
            ? new HashSet<string>(previous.Documents.Select(d => d.ContentPath).Append(FileName), StringComparer.Ordinal)
            : null;
}

/// <summary>One built document.</summary>
/// <param name="ContentPath">Relative to <c>corpus.json</c>.</param>
/// <param name="ContentSha256">The exact file that is uploaded. The same hash with different
/// metadata is still a change to publish.</param>
public sealed record CorpusDocument(
    string DocId,
    string Title,
    string DocType,
    string Classification,
    string ContentPath,
    string ContentSha256,
    IReadOnlyList<SectionPath> SectionPaths);
