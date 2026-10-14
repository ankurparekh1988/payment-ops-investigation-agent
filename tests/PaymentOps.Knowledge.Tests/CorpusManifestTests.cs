using PaymentOps.Knowledge.Corpus;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Knowledge.Tests;

public sealed class CorpusManifestTests
{
    [Fact]
    public void Describes_each_source_by_its_content_file()
    {
        using var tree = new SourceTree().Add("restricted/missing-settlement-batch.md", SourceTree.Source());
        var source = Assert.Single(tree.Read());

        var document = Assert.Single(CorpusManifest.From([source]).Documents);

        Assert.Equal(
            ("missing-settlement-batch.md", "restricted", source.ContentSha256),
            (document.ContentPath, document.Classification, document.ContentSha256));
        Assert.Equal(source.SectionPaths, document.SectionPaths);
    }

    [Fact]
    public void Writes_the_content_files_and_the_manifest()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        using var output = new SourceTree();
        var sources = tree.Read();

        var manifest = CorpusManifest.Write(sources, output.Root);

        Assert.Equal(
            ["corpus.json", "missing-settlement-batch.md"],
            Directory.EnumerateFiles(output.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var content = File.ReadAllBytes(Path.Combine(output.Root, "missing-settlement-batch.md"));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(sources[0].Content), content);
        Assert.Equal(manifest.ToJson(), File.ReadAllText(Path.Combine(output.Root, CorpusManifest.FileName)));
    }

    [Fact]
    public void A_rebuild_removes_documents_that_are_gone()
    {
        using var output = new SourceTree().Add("corpus.json", "{}").Add("removed-runbook.md", "# Old\n");
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());

        CorpusManifest.Write(tree.Read(), output.Root);

        Assert.False(File.Exists(Path.Combine(output.Root, "removed-runbook.md")));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("corpus.json", "nested/x.md")]
    public void Refuses_to_empty_a_directory_it_did_not_write(params string[] existing)
    {
        using var output = new SourceTree();
        foreach (var path in existing)
        {
            output.Add(path, "x");
        }

        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());

        var error = Assert.Throws<KnowledgeCorpusException>(() => CorpusManifest.Write(tree.Read(), output.Root));
        Assert.Contains("isn't a previous corpus build", error.Errors[0], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output.Root, existing[0])));
    }

    [Fact]
    public void Serializes_the_same_bytes_whatever_the_document_order()
    {
        using var tree = new SourceTree()
            .Add("general/a-runbook.md", SourceTree.Source(docId: "a-runbook"))
            .Add("general/b-runbook.md", SourceTree.Source(docId: "b-runbook"));
        var sources = tree.Read();

        var forwards = CorpusManifest.From(sources).ToJson();
        var backwards = CorpusManifest.From(sources.Reverse()).ToJson();

        Assert.Equal(forwards, backwards);
        Assert.True(forwards.IndexOf("\"a-runbook\"", StringComparison.Ordinal) < forwards.IndexOf("\"b-runbook\"", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', forwards);
        Assert.EndsWith("}\n", forwards, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_camel_case_names()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());

        var json = CorpusManifest.From(tree.Read()).ToJson();

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"contentSha256\": \"sha256:", json, StringComparison.Ordinal);
        Assert.Contains("\"h1\": \"Missing settlement batch\"", json, StringComparison.Ordinal);
    }
}

/// <summary>The corpus in the repository is valid.</summary>
public sealed class ShippedCorpusTests
{
    [Fact]
    public void The_repository_sources_are_valid()
    {
        var sources = KnowledgeSourceReader.Read(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src"));

        Assert.Contains(sources, s => s.Classification == KnowledgeClassification.General);
        Assert.Contains(sources, s => s.Classification == KnowledgeClassification.Restricted);
    }
}
