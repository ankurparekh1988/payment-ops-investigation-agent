using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeSectionTests
{
    [Fact]
    public void Each_heading_owns_the_text_up_to_the_next_heading()
    {
        var sections = KnowledgeSections.Split(SourceTree.Body.ReplaceLineEndings("\n") + "\n");

        Assert.Equal(
            [
                (new SectionPath("Missing settlement batch", "", ""), 0, ""),
                (new SectionPath("Missing settlement batch", "Confirm the batch is missing", ""), 1, "Check the batches received for the settlement date."),
                (new SectionPath("Missing settlement batch", "Confirm the batch is missing", "Partial files"), 2, "A second file can complete the batch."),
            ],
            sections.Select(s => (s.Path, s.Ordinal, s.Content)));
    }

    [Fact]
    public void A_section_of_only_a_list_or_code_keeps_its_text()
    {
        const string Content = "# T\n\n## Steps\n\n- one\n- two\n\n## Command\n\n    run it\n";

        var sections = KnowledgeSections.Split(Content);

        Assert.Equal("- one\n- two", sections[1].Content);
        Assert.Equal("    run it", sections[2].Content);
    }

    [Fact]
    public void A_trailing_heading_without_text_is_skipped()
    {
        var sections = KnowledgeSections.Split("# T\n\nIntro.\n\n## Empty\n");

        Assert.Equal([true, false], sections.Select(s => s.HasText));
    }

    [Fact]
    public void The_shipped_corpus_splits_into_its_section_paths()
    {
        var sources = KnowledgeSourceReader.Read(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src"));

        Assert.All(sources, source =>
            Assert.Equal(source.SectionPaths, KnowledgeSections.Split(source.Content).Select(s => s.Path)));
        Assert.InRange(sources.Sum(s => KnowledgeSections.Split(s.Content).Count(section => section.HasText)), 1, 500);
    }
}
