using PaymentOps.Knowledge.Corpus;
using PaymentOps.Platform.Text;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeSourceReaderTests
{
    [Fact]
    public void Reads_metadata_and_section_paths()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());

        var source = Assert.Single(tree.Read());

        Assert.Equal(("missing-settlement-batch", "Missing settlement batch", "runbook", "general"), (source.DocId, source.Title, source.DocType, source.Classification));
        Assert.Equal("general/missing-settlement-batch.md", source.SourcePath);
        Assert.Equal(
            [
                new SectionPath("Missing settlement batch", "", ""),
                new SectionPath("Missing settlement batch", "Confirm the batch is missing", ""),
                new SectionPath("Missing settlement batch", "Confirm the batch is missing", "Partial files"),
            ],
            source.SectionPaths);
    }

    [Fact]
    public void The_content_is_the_body_alone_and_is_what_the_hash_covers()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: SourceTree.Body + "\n\n\n"));

        var source = Assert.Single(tree.Read());

        Assert.Equal(SourceTree.Body + "\n", source.Content);
        Assert.Equal(SourceText.Sha256(source.Content), source.ContentSha256);
    }

    [Fact]
    public void Front_matter_changes_leave_the_content_hash_alone()
    {
        using var runbook = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        using var policy = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(docType: "policy"));

        Assert.Equal(runbook.Read()[0].ContentSha256, policy.Read()[0].ContentSha256);
    }

    [Fact]
    public void Classification_comes_from_the_folder_and_moving_a_file_keeps_its_content_hash()
    {
        using var general = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source());
        using var restricted = new SourceTree().Add("restricted/missing-settlement-batch.md", SourceTree.Source());

        var before = Assert.Single(general.Read());
        var after = Assert.Single(restricted.Read());

        Assert.Equal((KnowledgeClassification.General, KnowledgeClassification.Restricted), (before.Classification, after.Classification));
        Assert.Equal(before.ContentSha256, after.ContentSha256);
    }

    [Fact]
    public void Line_endings_do_not_change_the_content_hash()
    {
        var lf = SourceTree.Source();
        using var unix = new SourceTree().Add("general/missing-settlement-batch.md", lf);
        using var windows = new SourceTree().Add("general/missing-settlement-batch.md", lf.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(unix.Read()[0].ContentSha256, windows.Read()[0].ContentSha256);
    }

    [Fact]
    public void Documents_are_ordered_by_doc_id()
    {
        using var tree = new SourceTree()
            .Add("restricted/a-policy.md", SourceTree.Source(docId: "a-policy"))
            .Add("general/b-runbook.md", SourceTree.Source(docId: "b-runbook"));

        Assert.Equal(["a-policy", "b-runbook"], tree.Read().Select(s => s.DocId));
    }

    [Theory]
    [InlineData("notes.txt", "'notes.txt' isn't expected")]
    [InlineData("secret/x.md", "'secret' isn't expected")]
    [InlineData("general/readme.txt", "general/readme.txt: only Markdown files")]
    [InlineData("general/nested/x.md", "general/nested: only Markdown files")]
    public void Anything_outside_the_layout_is_rejected(string path, string expected)
    {
        using var tree = new SourceTree().Add(path, "x");

        Assert.Contains(tree.Errors(), e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown: x\n", "unknown")]
    [InlineData("docId: again\n", "Duplicate key")]
    public void Front_matter_is_strict(string extra, string expected)
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(extraFrontMatter: extra));

        Assert.Contains(tree.Errors(), e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Front_matter_fields_are_required()
    {
        using var tree = new SourceTree().Add("general/x.md", "---\ndocId: x\ntitle: X\n---\n\n# X\n");

        Assert.Contains(tree.Errors(), e => e.Contains("docType", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("general/other-name.md", "missing-settlement-batch", "the file name must be the docId")]
    [InlineData("general/Bad_Id.md", "Bad_Id", "must be lowercase letters and digits")]
    public void The_doc_id_is_the_file_name(string path, string docId, string expected)
    {
        using var tree = new SourceTree().Add(path, SourceTree.Source(docId: docId));

        Assert.Contains(tree.Errors(), e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_doc_type_comes_from_a_fixed_list()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(docType: "guide"));

        Assert.Contains(tree.Errors(), e => e.Contains("docType 'guide' must be one of", StringComparison.Ordinal));
    }

    [Fact]
    public void A_doc_id_is_unique_across_classifications()
    {
        using var tree = new SourceTree()
            .Add("general/missing-settlement-batch.md", SourceTree.Source())
            .Add("restricted/missing-settlement-batch.md", SourceTree.Source());

        Assert.Contains(tree.Errors(), e => e.Contains("docId 'missing-settlement-batch' is used by general/missing-settlement-batch.md and restricted/missing-settlement-batch.md", StringComparison.Ordinal));
    }

    [Fact]
    public void The_corpus_has_a_document_cap()
    {
        using var tree = new SourceTree();
        for (var i = 0; i <= CorpusLimits.MaxDocuments; i++)
        {
            tree.Add($"general/doc-{i}.md", SourceTree.Source(docId: $"doc-{i}"));
        }

        Assert.Contains(tree.Errors(), e => e.Contains($"the cap is {CorpusLimits.MaxDocuments}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_has_a_size_cap()
    {
        var body = SourceTree.Body + "\n\n" + new string('x', CorpusLimits.MaxContentBytes);
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: body));

        Assert.Contains(tree.Errors(), e => e.Contains("split it into separate documents", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_corpus_is_rejected()
    {
        using var tree = new SourceTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "general"));

        Assert.Contains(tree.Errors(), e => e.Contains("has no documents", StringComparison.Ordinal));
    }

    [Fact]
    public void A_byte_order_mark_is_rejected()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(SourceTree.Source())]);

        Assert.Contains(tree.Errors(), e => e.Contains("byte-order mark", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_error_is_reported_together()
    {
        using var tree = new SourceTree()
            .Add("general/a.md", SourceTree.Source(docId: "a", docType: "guide"))
            .Add("general/b.md", SourceTree.Source(docId: "b", extraFrontMatter: "unknown: x\n"));

        Assert.Equal(2, tree.Errors().Count);
    }
}
