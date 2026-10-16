using PaymentOps.Knowledge.Corpus;
using PaymentOps.Platform.Text;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// The search documents a corpus build should produce. The build is checked again first, so content
/// changed after the build, or a corpus.json that doesn't describe its content, is never published.
/// </summary>
public static class IndexedCorpus
{
    public static IReadOnlyList<KnowledgeSearchDocument> Load(string corpusDirectory)
    {
        ArgumentNullException.ThrowIfNull(corpusDirectory);

        var manifest = CorpusManifest.TryRead(corpusDirectory)
            ?? throw new KnowledgeCorpusException([$"'{corpusDirectory}' has no {CorpusManifest.FileName} this version reads; build the corpus first."]);

        var errors = new List<string>();
        var entries = new List<(CorpusDocument, string)>();
        foreach (var entry in manifest.Documents)
        {
            var path = Path.Combine(corpusDirectory, entry.ContentPath);
            if (!File.Exists(path))
            {
                errors.Add($"{entry.ContentPath} is listed in {CorpusManifest.FileName} but missing.");
            }
            else if (SourceText.Decode(File.ReadAllBytes(path), entry.ContentPath, errors) is { } content)
            {
                entries.Add((entry, content));
            }
        }

        return Build(entries, errors);
    }

    internal static IReadOnlyList<KnowledgeSearchDocument> FromSources(IEnumerable<KnowledgeSource> sources)
    {
        var list = sources.ToList();
        return Build([.. CorpusManifest.From(list).Documents.Zip(list, (entry, source) => (entry, source.Content))], []);
    }

    private static List<KnowledgeSearchDocument> Build(List<(CorpusDocument Entry, string Content)> entries, List<string> errors)
    {
        var documents = new List<KnowledgeSearchDocument>();
        foreach (var (entry, content) in entries)
        {
            documents.AddRange(DocumentsFor(entry, content, errors));
        }

        foreach (var duplicate in documents.GroupBy(d => d.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"Section '{duplicate.First().Describe()}' occurs more than once.");
        }

        // Publishing nothing would delete every section in the index.
        if (entries.Count == 0 && errors.Count == 0)
        {
            errors.Add("The corpus has no documents; refusing to publish an empty index.");
        }

        if (errors.Count > 0)
        {
            throw new KnowledgeCorpusException(errors);
        }

        return documents;
    }

    private static IEnumerable<KnowledgeSearchDocument> DocumentsFor(CorpusDocument entry, string content, List<string> errors)
    {
        if (!KnowledgeClassification.All.Contains(entry.Classification, StringComparer.Ordinal)
            || !KnowledgeDocType.All.Contains(entry.DocType, StringComparer.Ordinal))
        {
            errors.Add($"{entry.ContentPath}: classification '{entry.Classification}' or docType '{entry.DocType}' isn't one this build knows.");
            return [];
        }

        if (SourceText.Sha256(content) != entry.ContentSha256)
        {
            errors.Add($"{entry.ContentPath}: the content doesn't match its contentSha256 in {CorpusManifest.FileName}.");
            return [];
        }

        var sections = KnowledgeSections.Split(content);
        if (!sections.Select(s => s.Path).SequenceEqual(entry.SectionPaths))
        {
            errors.Add($"{entry.ContentPath}: the headings don't match the section paths in {CorpusManifest.FileName}.");
            return [];
        }

        // The plan learns a document's classification from its sections, so one with none can't be tightened safely.
        var indexed = sections.Where(s => s.HasText).ToList();
        if (indexed.Count == 0)
        {
            errors.Add($"{entry.ContentPath}: no section has text of its own, so there's nothing to index.");
            return [];
        }

        return indexed.Select(s => new KnowledgeSearchDocument(
            SectionKey.For(entry.DocId, s.Path), entry.DocId, entry.Title, entry.DocType, entry.Classification,
            s.Path.H2, s.Path.H3, s.Path.ToString(), s.Ordinal, s.Content));
    }
}
