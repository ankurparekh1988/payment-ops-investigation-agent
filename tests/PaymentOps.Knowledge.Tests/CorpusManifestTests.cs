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
        using var output = new SourceTree();
        using (var before = new SourceTree()
            .Add("general/missing-settlement-batch.md", SourceTree.Source())
            .Add("general/removed-runbook.md", SourceTree.Source(docId: "removed-runbook")))
        {
            CorpusManifest.Write(before.Read(), output.Root);
        }

        using var after = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        CorpusManifest.Write(after.Read(), output.Root);

        Assert.Equal(
            ["corpus.json", "missing-settlement-batch.md"],
            Directory.EnumerateFiles(output.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("corpus.json", "notes.txt")]
    [InlineData("corpus.json", "nested/x.md")]
    public void Refuses_a_directory_without_a_readable_manifest(params string[] existing)
    {
        using var output = new SourceTree();
        foreach (var path in existing)
        {
            output.Add(path, "not a manifest");
        }

        AssertRefusedAndUntouched(output);
    }

    [Fact]
    public void Refuses_a_previous_build_with_an_unexpected_file()
    {
        using var output = PreviousBuild();
        output.Add("important.csv", "keep me");

        AssertRefusedAndUntouched(output);
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 99")]
    [InlineData("\"contentPath\": \"missing-settlement-batch.md\"", "\"contentPath\": \"../outside.md\"")]
    [InlineData("\"contentPath\": \"missing-settlement-batch.md\"", "\"contentPath\": \"corpus.json\"")]
    public void Refuses_a_manifest_it_does_not_trust(string original, string replacement)
    {
        using var output = PreviousBuild();
        var manifestPath = Path.Combine(output.Root, CorpusManifest.FileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace(original, replacement, StringComparison.Ordinal));

        AssertRefusedAndUntouched(output);
    }

    private static SourceTree PreviousBuild()
    {
        var output = new SourceTree();
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        CorpusManifest.Write(tree.Read(), output.Root);
        return output;
    }

    private static void AssertRefusedAndUntouched(SourceTree output)
    {
        static Dictionary<string, string> Snapshot(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);

        var before = Snapshot(output.Root);
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: SourceTree.Body + "\n\nChanged.\n"));

        var error = Assert.Throws<KnowledgeCorpusException>(() => CorpusManifest.Write(tree.Read(), output.Root));

        Assert.Contains("isn't exactly a previous corpus build", error.Errors[0], StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(output.Root));
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
