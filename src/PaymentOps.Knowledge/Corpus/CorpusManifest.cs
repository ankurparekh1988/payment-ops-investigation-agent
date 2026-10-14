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

    // Only a directory a previous build wrote (it has corpus.json and no subfolders) is ever emptied.
    private static void PrepareOutput(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            if (!File.Exists(Path.Combine(outputDirectory, FileName)) || Directory.EnumerateDirectories(outputDirectory).Any())
            {
                throw new KnowledgeCorpusException([$"Output directory '{outputDirectory}' isn't empty and isn't a previous corpus build; choose another."]);
            }

            foreach (var file in Directory.EnumerateFiles(outputDirectory))
            {
                File.Delete(file);
            }
        }

        Directory.CreateDirectory(outputDirectory);
    }
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
