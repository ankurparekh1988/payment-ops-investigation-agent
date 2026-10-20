using System.Text.Json;

using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Tests;

public sealed class RetrievalCompareTests
{
    private static readonly CalibrationResult Result = CalibrationDecision.Decide(
    [
        new CaseRun("c1", CaseKind.Critical, CalibrationDecision.Lexical, [new("doc--a", "doc", "Doc > A", "general", null)], 1, 1, false),
        new CaseRun("c1", CaseKind.Critical, CalibrationDecision.Semantic, [new("doc--a", "doc", "Doc > A", "general", 3.1)], 1, 1, false) { ExpectedScores = [3.1] },
    ]);

    [Fact]
    public async Task Each_case_runs_in_both_modes_through_knowledge_access()
    {
        var dataset = new CalibrationDataset(1,
        [
            new("c1", CaseKind.Critical, "restricted", "q1", [new("doc", "Doc > A")]),
            new("p1", CaseKind.Probe, "general", "q2", []),
        ]);
        var calls = new List<(string Question, bool Restricted, RetrievalMode Mode)>();

        var result = await RetrievalCompare.RunAsync(dataset, new Dictionary<ExpectedSection, string> { [new("doc", "Doc > A")] = "doc--a" }, (question, access, mode) =>
        {
            calls.Add((question, access.IncludesRestricted, mode));
            IReadOnlyList<RetrievedSection> hits = question == "q1" ? [new("doc--a", "doc", "Doc", "Doc > A", "text", "restricted", null)] : [];
            return Task.FromResult(hits);
        });

        Assert.Equal(
            [("q1", true, RetrievalMode.Lexical), ("q1", true, RetrievalMode.Semantic), ("q2", false, RetrievalMode.Lexical), ("q2", false, RetrievalMode.Semantic)],
            calls);
        Assert.Equal(4, result.Runs.Count);
        Assert.All(result.Runs.Where(r => r.CaseId == "c1"), r => Assert.Equal(1, r.ExpectedRank));
        Assert.True(result.ProbesPassed);
    }

    [Fact]
    public void The_json_and_the_summary_come_from_the_same_result()
    {
        var json = JsonDocument.Parse(CalibrationReport.ToJson(Result)).RootElement;
        var markdown = CalibrationReport.ToMarkdown(Result);

        Assert.Equal(Result.RecommendedMode, json.GetProperty("decision").GetProperty("recommendedMode").GetString());
        Assert.True(json.GetProperty("observations").TryGetProperty("runs", out _));
        Assert.Contains($"Recommended mode: **{Result.RecommendedMode}**", markdown, StringComparison.Ordinal);
        Assert.Contains("| c1 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_identifying_the_caller_or_section_text_is_written()
    {
        var text = CalibrationReport.ToJson(Result) + CalibrationReport.ToMarkdown(Result);

        foreach (var forbidden in new[] { "token", "authorization", "bearer", "tenant", "client", "content" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Calibration_users_are_signed_in_with_only_their_access_class()
    {
        Assert.True(KnowledgeAccess.From(CalibrationUser.For("restricted")).IncludesRestricted);
        Assert.False(KnowledgeAccess.From(CalibrationUser.For("general")).IncludesRestricted);
    }
}
