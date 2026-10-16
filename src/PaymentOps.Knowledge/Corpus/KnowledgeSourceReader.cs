using System.Text;
using System.Text.RegularExpressions;

using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Text;

namespace PaymentOps.Knowledge.Corpus;

/// <summary>
/// Reads the knowledge sources under a directory laid out as <c>general/</c> and <c>restricted/</c>,
/// and validates all of them. Any problem stops the read with every error found.
/// </summary>
public static partial class KnowledgeSourceReader
{
    public static IReadOnlyList<KnowledgeSource> Read(string sourceRoot)
    {
        ArgumentNullException.ThrowIfNull(sourceRoot);

        var errors = new List<string>();
        if (!Directory.Exists(sourceRoot))
        {
            throw new KnowledgeCorpusException([$"Source directory '{sourceRoot}' doesn't exist."]);
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceRoot).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entry);
            if (!Directory.Exists(entry) || !KnowledgeClassification.All.Contains(name, StringComparer.Ordinal))
            {
                errors.Add($"'{name}' isn't expected in the source directory; it holds only {string.Join(" and ", KnowledgeClassification.All.Select(c => $"'{c}/'"))}.");
            }
        }

        var sources = new List<KnowledgeSource>();
        foreach (var classification in KnowledgeClassification.All)
        {
            var folder = Path.Combine(sourceRoot, classification);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
            {
                var path = $"{classification}/{Path.GetFileName(entry)}";
                if (Directory.Exists(entry) || !path.EndsWith(".md", StringComparison.Ordinal))
                {
                    errors.Add($"{path}: only Markdown files belong here, with no subfolders.");
                    continue;
                }

                if (ReadSource(File.ReadAllBytes(entry), path, classification, errors) is { } source)
                {
                    sources.Add(source);
                }
            }
        }

        foreach (var duplicate in sources.GroupBy(s => s.DocId, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"docId '{duplicate.Key}' is used by {string.Join(" and ", duplicate.Select(s => s.SourcePath))}.");
        }

        // An empty corpus is almost certainly a wrong path, and publishing it would remove every document.
        if (sources.Count == 0 && errors.Count == 0)
        {
            errors.Add($"Source directory '{sourceRoot}' has no documents.");
        }

        if (sources.Count > CorpusLimits.MaxDocuments)
        {
            errors.Add($"The corpus has {sources.Count} documents; the cap is {CorpusLimits.MaxDocuments}.");
        }

        if (errors.Count > 0)
        {
            throw new KnowledgeCorpusException(errors);
        }

        return [.. sources.OrderBy(s => s.DocId, StringComparer.Ordinal)];
    }

    // A source that adds errors is still returned; Read throws before any of them are used.
    internal static KnowledgeSource? ReadSource(byte[] bytes, string path, string classification, List<string> errors)
    {
        var text = SourceText.Decode(bytes, path, errors);
        if (text is null || SourceText.SplitFrontMatter(text, path, errors) is not var (frontMatterYaml, body, bodyLine))
        {
            return null;
        }

        var frontMatter = SourceText.Deserialize<SourceFrontMatter>(frontMatterYaml, path, errors);
        if (frontMatter is null)
        {
            return null;
        }

        CheckFrontMatter(frontMatter, path, classification, errors);

        // The front matter is our build metadata, so only the body is uploaded. Blank lines around it
        // are dropped and line numbers in errors still count from the top of the file.
        var content = body.TrimStart('\n');
        var contentFirstLine = bodyLine + (body.Length - content.Length);
        content = content.TrimEnd() + "\n";

        var sections = KnowledgeBody.Validate(content, frontMatter.Title, path, contentFirstLine, errors);
        PublicContentCheck.Check(content, path, contentFirstLine, errors);
        if (Encoding.UTF8.GetByteCount(content) > CorpusLimits.MaxContentBytes)
        {
            errors.Add($"{path}: the body is larger than {CorpusLimits.MaxContentBytes / 1024} KB; split it into separate documents.");
        }

        // Without an indexed section, nothing in the index would carry the document's classification.
        if (!KnowledgeSections.Split(content).Any(s => s.HasText))
        {
            errors.Add($"{path}: no section has text of its own, so there's nothing to index.");
        }

        return new KnowledgeSource(
            frontMatter.DocId,
            frontMatter.Title,
            frontMatter.DocType,
            classification,
            path,
            content,
            SourceText.Sha256(content),
            sections);
    }

    private static void CheckFrontMatter(SourceFrontMatter frontMatter, string path, string classification, List<string> errors)
    {
        if (!DocIdPattern().IsMatch(frontMatter.DocId))
        {
            errors.Add($"{path}: docId '{frontMatter.DocId}' must be lowercase letters and digits in hyphen-separated words, at most 64 characters.");
        }

        if (path != $"{classification}/{frontMatter.DocId}.md")
        {
            errors.Add($"{path}: the file name must be the docId, '{frontMatter.DocId}.md'.");
        }

        if (!KnowledgeDocType.All.Contains(frontMatter.DocType, StringComparer.Ordinal))
        {
            errors.Add($"{path}: docType '{frontMatter.DocType}' must be one of {string.Join(", ", KnowledgeDocType.All)}.");
        }
    }

    [GeneratedRegex("^(?=.{1,64}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    internal static partial Regex DocIdPattern();
}

internal sealed class SourceFrontMatter
{
    public required string DocId { get; init; }

    public required string Title { get; init; }

    public required string DocType { get; init; }
}
