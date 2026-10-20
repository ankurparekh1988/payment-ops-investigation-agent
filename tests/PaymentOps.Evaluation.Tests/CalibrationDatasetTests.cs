using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Evaluation.Tests;

public sealed class CalibrationDatasetTests
{
    private static readonly string DatasetPath = Path.Combine(RepositoryRoot.FullPath, "eval", "datasets", "retrieval-comparison.json");
    private static readonly IReadOnlyList<KnowledgeSearchDocument> Sections =
        IndexedCorpus.FromSourceDirectory(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src"));

    [Fact]
    public void Every_expected_section_resolves_to_exactly_one_indexed_section()
    {
        var dataset = CalibrationDataset.Load(DatasetPath);

        var keys = CalibrationReferences.Resolve(dataset, Sections);

        Assert.Equal(dataset.Cases.SelectMany(c => c.Expected).Distinct().Count(), keys.Count);
    }

    [Fact]
    public void Every_document_has_a_critical_case_and_restricted_ones_are_probed()
    {
        var dataset = CalibrationDataset.Load(DatasetPath);
        var critical = dataset.Cases.Where(c => c.Kind == CaseKind.Critical).SelectMany(c => c.Expected).Select(e => e.DocId).ToHashSet(StringComparer.Ordinal);

        Assert.True(Sections.Select(s => s.DocId).ToHashSet(StringComparer.Ordinal).SetEquals(critical));
        Assert.Contains(dataset.Cases, c => c.Kind == CaseKind.Critical && c.Access == "restricted");
        Assert.Contains(dataset.Cases, c => c.Kind == CaseKind.Probe && c.Access == "general");
    }

    [Fact]
    public void Answerable_cases_expect_sections_and_the_rest_dont()
    {
        Assert.All(CalibrationDataset.Load(DatasetPath).Cases, c => Assert.Equal(c.IsAnswerable, c.Expected.Count > 0));
    }

    [Theory]
    [InlineData("missing-doc", "Missing doc > Nothing")]
    [InlineData("duplicate-settlement-batch", "Duplicate settlement batch > No such heading")]
    [InlineData("duplicate-settlement-batch", "Duplicate settlement batch > Tell a duplicate from a split batch")]
    public void A_bad_reference_fails_with_its_case(string docId, string path)
    {
        var dataset = new CalibrationDataset(1, [new CalibrationCase("bad-case", CaseKind.Critical, "general", "q", [new ExpectedSection(docId, path)])]);

        var ex = Assert.Throws<InvalidDataException>(() => CalibrationReferences.Resolve(dataset, Sections));

        Assert.Contains("bad-case", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_access_class_fails()
    {
        var dataset = new CalibrationDataset(1, [new CalibrationCase("odd", CaseKind.Unrelated, "admin", "q", [])]);

        Assert.Throws<InvalidDataException>(() => CalibrationReferences.Resolve(dataset, Sections));
    }
}
