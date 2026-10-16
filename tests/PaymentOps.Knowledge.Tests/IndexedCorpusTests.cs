using System.Text.Json.Nodes;

using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Text;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Knowledge.Tests;

public sealed class IndexedCorpusTests : IDisposable
{
    private readonly DirectoryInfo _build = Directory.CreateTempSubdirectory("knowledge-build-");

    public void Dispose() => _build.Delete(recursive: true);

    private string Build(SourceTree tree)
    {
        CorpusManifest.Write(tree.Read(), _build.FullName);
        return _build.FullName;
    }

    private string CorpusJsonPath => Path.Combine(_build.FullName, CorpusManifest.FileName);

    [Fact]
    public void Indexes_each_section_with_text_and_its_document_metadata()
    {
        using var tree = new SourceTree().Add("restricted/missing-settlement-batch.md", SourceTree.Source(docType: "policy"));

        var documents = IndexedCorpus.Load(Build(tree));

        Assert.Equal(["Missing settlement batch > Confirm the batch is missing", "Missing settlement batch > Confirm the batch is missing > Partial files"], documents.Select(d => d.SectionPath));
        Assert.All(documents, d =>
        {
            Assert.Equal("missing-settlement-batch", d.DocId);
            Assert.Equal("Missing settlement batch", d.Title);
            Assert.Equal("policy", d.DocType);
            Assert.Equal(KnowledgeClassification.Restricted, d.Classification);
        });
        Assert.Equal(("Confirm the batch is missing", "Partial files", 2), (documents[1].H2, documents[1].H3, documents[1].Ordinal));
        Assert.Equal(SectionKey.For("missing-settlement-batch", new("Missing settlement batch", "Confirm the batch is missing", "Partial files")), documents[1].Id);
    }

    [Fact]
    public void Content_that_doesnt_match_its_hash_is_refused()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        var directory = Build(tree);
        File.AppendAllText(Path.Combine(directory, "missing-settlement-batch.md"), "\nAdded after the build.\n");

        var errors = Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.Load(directory)).Errors;

        Assert.Contains(errors, e => e.Contains("contentSha256", StringComparison.Ordinal));
    }

    [Fact]
    public void Section_paths_that_dont_match_the_content_are_refused()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        var directory = Build(tree);
        var json = JsonNode.Parse(File.ReadAllText(CorpusJsonPath))!;
        json["documents"]![0]!["sectionPaths"]![2]!["h3"] = "Renamed";
        File.WriteAllText(CorpusJsonPath, json.ToJsonString());

        var errors = Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.Load(directory)).Errors;

        Assert.Contains(errors, e => e.Contains("section paths", StringComparison.Ordinal));
    }

    [Fact]
    public void A_content_path_outside_the_build_is_refused()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        var directory = Build(tree);
        var json = JsonNode.Parse(File.ReadAllText(CorpusJsonPath))!;
        json["documents"]![0]!["contentPath"] = "../missing-settlement-batch.md";
        File.WriteAllText(CorpusJsonPath, json.ToJsonString());

        Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.Load(directory));
    }

    [Fact]
    public void Duplicate_keys_are_refused_before_anything_is_published()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        var directory = Build(tree);
        var json = JsonNode.Parse(File.ReadAllText(CorpusJsonPath))!;
        var documents = json["documents"]!.AsArray();
        documents.Add(documents[0]!.DeepClone());
        File.WriteAllText(CorpusJsonPath, json.ToJsonString());

        var errors = Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.Load(directory)).Errors;

        Assert.Contains(errors, e => e.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_directory_that_isnt_a_corpus_build_is_refused()
    {
        Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.Load(_build.FullName));
    }

    [Fact]
    public void A_corpus_with_no_documents_is_refused()
    {
        CorpusManifest.Write([], _build.FullName);

        var errors = Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.Load(_build.FullName)).Errors;

        Assert.Contains(errors, e => e.Contains("no documents", StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_with_no_text_to_index_is_refused_beside_others()
    {
        // Built directly: the source reader refuses this document before the publisher could see it.
        const string content = "# Empty runbook\n\n## Steps\n";
        var empty = new KnowledgeSource("empty-runbook", "Empty runbook", "runbook", KnowledgeClassification.Restricted, "restricted/empty-runbook.md",
            content, SourceText.Sha256(content), [new("Empty runbook", "", ""), new("Empty runbook", "Steps", "")]);
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());

        var errors = Assert.Throws<KnowledgeCorpusException>(() => IndexedCorpus.FromSources([.. tree.Read(), empty])).Errors;

        Assert.Equal(["empty-runbook.md: no section has text of its own, so there's nothing to index."], errors);
    }

    [Fact]
    public void The_shipped_corpus_loads()
    {
        CorpusManifest.Write(KnowledgeSourceReader.Read(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src")), _build.FullName);

        var documents = IndexedCorpus.Load(_build.FullName);

        Assert.Contains(documents, d => d.Classification == KnowledgeClassification.Restricted);
        Assert.Equal(documents.Count, documents.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
    }
}
