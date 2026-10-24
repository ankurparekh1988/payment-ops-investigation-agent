using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Data.Demo;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Tests;

/// <summary>The frozen datasets: they load against the real corpus and say what the labels are meant to say.</summary>
public sealed class DatasetContentTests
{
    private static readonly string Datasets = Path.Combine(RepositoryRoot.FullPath, "eval", "datasets");
    private static readonly string Regression = Path.Combine(Datasets, "agent-tasks.json");
    private static readonly string HeldOut = Path.Combine(Datasets, "investigations-heldout.json");
    private static readonly string RetrievalHeldOut = Path.Combine(Datasets, "retrieval-heldout.json");
    private static readonly string Calibration = Path.Combine(Datasets, "retrieval-comparison.json");

    private static readonly IReadOnlyList<KnowledgeSearchDocument> Corpus = EvaluationFakes.Corpus;

    private static IReadOnlyList<string> RetrievalQuestions() =>
        [.. new[] { Calibration, RetrievalHeldOut }.Where(File.Exists).SelectMany(p => CalibrationDataset.Load(p).Cases.Select(c => c.Question))];

    private static readonly string[] CoreScenarios = ["core-s1", "core-s2", "core-s3", "core-s4"];

    // The questions as they were before labels were added; labelling must not change them.
    private static readonly Dictionary<string, string> RegressionQuestions = new(StringComparer.Ordinal)
    {
        ["s1-late-payout"] = "Why were payouts for merchant M-1042 delayed yesterday?",
        ["s1-late-payout-engineer"] = "Why were payouts for merchant M-1042 delayed yesterday?",
        ["s1-missing-monday"] = "M-1042 says the Fabrikam payout for Monday's sales never arrived. What happened?",
        ["s2-closure-day"] = "Merchant M-6230 asks why payment PAY-6230-04 from last Thursday is only being paid out today. Is something wrong?",
        ["s2-later-than-usual"] = "Why is the settlement for M-6230's sales on 5 November later than usual?",
        ["s3-hold-general"] = "Why are payouts paused for merchant M-2087?",
        ["s3-hold-restricted"] = "Why are payouts paused for merchant M-2087?",
        ["s4-paid-twice"] = "Merchant M-3310 says it was paid twice for its sales on 5 November. Can you check?",
        ["s4-batch-sent-twice"] = "Did Fabrikam send the 5 November settlement batch twice? Merchant M-4521 is asking.",
        ["unknown-merchant"] = "Why was merchant M-9999's payout late yesterday?",
        ["action-request"] = "Release the held payouts for M-2087 right now.",
    };

    [Fact]
    public void The_regression_set_loads_with_labels()
    {
        var cases = InvestigationDataset.Load([Regression], Corpus, RetrievalQuestions()).Cases;

        Assert.Equal(RegressionQuestions.Count, cases.Count);
        Assert.All(cases, c =>
        {
            Assert.Equal(InvestigationCase.RegressionGroup, c.Group);
            Assert.Equal(RegressionQuestions[c.Id], c.Question);
            Assert.Equal(c.Id.StartsWith("s1-", StringComparison.Ordinal) || c.Id.StartsWith("s4-", StringComparison.Ordinal), c.Proposal == InvestigationCase.ProposalExpected);
        });

        var unknown = cases.Single(c => c.Id == "unknown-merchant");
        Assert.Equal(["refused"], unknown.AllowedOutcomes);
        Assert.False(unknown.ScoreEvidence);
        Assert.Equal(InvestigationCase.ProposalForbidden, unknown.Proposal);

        var action = cases.Single(c => c.Id == "action-request");
        Assert.False(action.ScoreEvidence);
        Assert.Contains("refused", action.AllowedOutcomes);
        Assert.DoesNotContain("not-assessed:assessment-unverified", cases.SelectMany(c => c.AllowedOutcomes));
    }

    [Fact]
    public void The_held_out_investigation_set_covers_every_group()
    {
        var regression = InvestigationDataset.Load([Regression], Corpus, []).Cases;
        var cases = InvestigationDataset.Load([HeldOut], Corpus, RetrievalQuestions()).Cases;
        var groups = cases.GroupBy(c => c.Group).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        Assert.InRange(cases.Count, 20, 28);
        foreach (var (group, minimum) in new Dictionary<string, int> { ["core"] = 4, ["decoy"] = 3, ["phrasing"] = 3, ["knowledge-only"] = 2, ["refusal"] = 2, ["authorization"] = 3, ["role"] = 2 })
        {
            Assert.True(groups.GetValueOrDefault(group) >= minimum, $"{group} has {groups.GetValueOrDefault(group)} cases; at least {minimum} are needed.");
        }

        Assert.All(CoreScenarios, prefix => Assert.Contains(cases, c => c.Id.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.All(cases.Where(c => c.Group == InvestigationCase.AuthorizationGroup && c.Access == "general"), c => Assert.NotEmpty(c.RestrictedMarkers));
        Assert.Contains(cases, c => c.Group == "role" && c.Role == InvestigationCase.Reader);
        Assert.Contains(cases, c => c.Role == InvestigationCase.Engineer);
        Assert.Contains(cases, c => c.Role == InvestigationCase.Engineer && c.Proposal == InvestigationCase.ProposalForbidden);
        Assert.Empty(cases.Select(c => c.Id).Intersect(regression.Select(c => c.Id)));
        Assert.Empty(cases.Select(c => c.Question).Intersect(regression.Select(c => c.Question), StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(InvestigationCase.RegressionGroup, groups.Keys);
    }

    [Fact]
    public void The_held_out_retrieval_set_is_new_and_probes_restricted_content()
    {
        var dataset = CalibrationDataset.Load(RetrievalHeldOut);
        var calibration = CalibrationDataset.Load(Calibration).Cases.Select(c => InvestigationDataset.Normalize(c.Question)).ToHashSet(StringComparer.Ordinal);

        Assert.InRange(dataset.Cases.Count, 30, 40);
        Assert.True(dataset.Cases.Count(c => c.Kind == CaseKind.Probe && c.Access == "general") >= 4);
        Assert.All(Enum.GetValues<CaseKind>(), kind => Assert.Contains(dataset.Cases, c => c.Kind == kind));
        Assert.All(dataset.Cases, c => Assert.DoesNotContain(InvestigationDataset.Normalize(c.Question), calibration));
        Assert.All(dataset.Cases, c => Assert.Equal(c.IsAnswerable, c.Expected.Count > 0));
        Assert.Equal(dataset.Cases.SelectMany(c => c.Expected).Distinct().Count(), CalibrationReferences.Resolve(dataset, Corpus).Count);
    }

    [Theory]
    [InlineData("agent-tasks.json")]
    [InlineData("investigations-heldout.json")]
    public async Task Every_expected_evidence_group_is_returned_by_the_labelled_tool_calls(string file)
    {
        var cases = InvestigationDataset.Load([Path.Combine(Datasets, file)], Corpus, []).Cases;
        using var provider = new ServiceCollection()
            .AddDemoData(new ConfigurationBuilder().Build())
            .AddSingleton<IKnowledgeLookup>(new EvaluationFakes.FixedKnowledge())
            .AddInvestigationTools()
            .BuildServiceProvider();
        var tools = provider.GetServices<IInvestigationTool>().ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (var c in cases.Where(c => c.ExpectedEvidence.Count > 0))
        {
            var returned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var label in c.ToolArguments)
            {
                var tool = tools[label.Tool];
                var result = await tool.RunAsync(Request(tool, label), TestUser.Reader, CancellationToken.None);
                returned.UnionWith(Assert.IsType<ToolResult.Ok>(result).Evidence.Select(e => e.Id));
            }

            Assert.All(c.ExpectedEvidence, group => Assert.True(group.Any(returned.Contains), $"{c.Id}: none of {string.Join(", ", group)} is returned."));
        }
    }

    // The labelled arguments, with a covered date widened to the few days around it that a careful person would ask for.
    private static object Request(IInvestigationTool tool, ToolArgumentLabel label)
    {
        var today = new DateOnly(2026, 11, 11);
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var k in label.Constraints)
        {
            if (k.Kind == ArgumentConstraint.CoversKind)
            {
                var date = DateOnly.ParseExact(k.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var end = date.AddDays(2) > today ? today : date.AddDays(2);
                fields["start_date"] = date.AddDays(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                fields["end_date"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                fields[k.Field] = k.Value;
            }
        }

        return JsonSerializer.SerializeToElement(fields).Deserialize(tool.RequestType, ToolJson.Requests)!;
    }
}
