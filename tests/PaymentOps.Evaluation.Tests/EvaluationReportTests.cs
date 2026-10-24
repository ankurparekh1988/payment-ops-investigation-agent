using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Tests;

public sealed class EvaluationReportTests
{
    private const string Sentinel = "MODEL-TEXT-SENTINEL";

    internal static readonly ExpectedSection Runbook = new("runbook-a", "A");

    internal static readonly LoadedCases Cases = new(
        [
            EvaluationFakes.Case("reg-1") with { Group = InvestigationCase.RegressionGroup },
            EvaluationFakes.Case("held-1") with { ScoreEvidence = true, ExpectedEvidence = [["merchant:M-1042"]], RequiredSections = [Runbook], AcceptableSections = [Runbook], ExpectedRootCause = "x" },
        ],
        new Dictionary<ExpectedSection, string> { [Runbook] = "ka" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["ka"] = "general" });

    internal static readonly ReportContext Context = new(
        "agent",
        new string('c', 40),
        [new PromptInfo("investigation-agent", "1.1.0")],
        new ModelInfo("chat-blue", "gpt-5.4-mini", "2026-03-17", "GlobalStandard"),
        new ModelInfo("judge", "gpt-5.4-mini", "2026-03-17", "GlobalStandard"),
        "1.0.0",
        new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    internal static RunOutcome Run(string heldOutcome = "assessed", bool leak = false) => new(
        [
            new CaseResult("reg-1", "assessed")
            {
                Calls = [new ToolCallRecord(ToolNames.MerchantProfile, new Dictionary<string, string?> { ["merchant_id"] = "M-1042" })],
                LatencyMs = 4000,
                TtfpMs = 900,
                InputTokens = 10_000,
                OutputTokens = 1_000,
                AssessmentText = Sentinel,
                UserVisibleText = Sentinel,
                CitedEvidenceFacts = new Dictionary<string, string> { ["merchant:M-1042"] = Sentinel },
            },
            new CaseResult("held-1", heldOutcome)
            {
                AssessmentEvidenceIds = ["merchant:M-1042"],
                AssessmentSectionKeys = ["ka"],
                RetrievedSections = leak ? [new SectionHit("kr", "restricted")] : [],
                LatencyMs = 8000,
                InputTokens = 20_000,
                OutputTokens = 2_000,
            },
        ],
        0);

    internal static EvaluationReport Report(params RunOutcome[] runs) =>
        EvaluationReport.ForInvestigations(Context, Cases, runs, [.. runs.Select(_ => new Dictionary<string, JudgeScore>())], Prices(), null, null);

    internal static PriceList Prices() => new([new Price("gpt-5.4-mini", "2026-03-17", "GlobalStandard", "USD", 0.75m, 0.075m, 4.5m, "2026-03-01", "2026-10-23", "https://prices.azure.com/api/retail/prices")]);

    [Fact]
    public void The_report_never_contains_model_text()
    {
        var report = Report(Run(), Run(), Run());

        Assert.DoesNotContain(Sentinel, report.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, ReportMarkdown.Investigations(report), StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_shows_regression_and_held_out_separately()
    {
        var report = Report(Run("incomplete"), Run(), Run());

        Assert.Equal(1, report.Breakdown[EvaluationReport.RegressionSet][MetricNames.ValidatedResultRate].Median);
        Assert.Equal(1, report.Breakdown[EvaluationReport.HeldOutSet][MetricNames.ValidatedResultRate].Median);
        Assert.Equal(0, report.Breakdown[EvaluationReport.HeldOutSet][MetricNames.ExpectedEvidenceCoverage].Min);
        Assert.True(report.Breakdown.ContainsKey("group:core"));
        var markdown = ReportMarkdown.Investigations(report);
        Assert.Contains("| Metric | Regression | Held-out |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_stability_view_lists_each_run_per_case()
    {
        var report = Report(Run("incomplete"), Run(), Run());

        var held = report.Stability.Single(s => s.CaseId == "held-1");
        Assert.Equal(["incomplete", "assessed", "assessed"], held.Outcomes);
        Assert.Equal([false, true, true], held.Metrics[MetricNames.ExpectedEvidenceCoverage]);
        Assert.Contains("| held-1 | held-out | incomplete", ReportMarkdown.Investigations(report), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_thresholds_the_gate_is_report_only_but_hard_gates_still_fail_it()
    {
        Assert.Equal(GateReport.ReportOnly, Report(Run(), Run(), Run()).Gate.Verdict);
        Assert.Equal(GateReport.Failed, Report(Run(), Run(leak: true), Run()).Gate.Verdict);
    }

    [Fact]
    public void Cost_latency_and_time_to_first_progress_are_summarized()
    {
        var report = Report(Run(), Run(), Run());

        // Six cases: 3 x (30k input, 3k output) = 90k x 0.75 + 9k x 4.5 per million.
        Assert.Equal(0.108m, report.Cost.Chat);
        Assert.Equal(0.018m, report.Cost.PerValidatedInvestigation);
        Assert.Equal(new Percentiles(4000, 8000), report.Latency);
        Assert.Equal(new Percentiles(900, 900), report.TimeToFirstProgress);
    }

    [Fact]
    public void An_unpriced_model_shows_not_priced()
    {
        var report = EvaluationReport.ForInvestigations(Context, Cases, [Run()], [new Dictionary<string, JudgeScore>()], new PriceList([]), null, null);

        Assert.Null(report.Cost.Chat);
        Assert.Contains("not priced", ReportMarkdown.Investigations(report), StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_reads_back_as_written()
    {
        var path = Path.GetTempFileName();
        var report = Report(Run(), Run(), Run());
        File.WriteAllText(path, report.ToJson());

        var loaded = EvaluationReport.Load(path);

        Assert.Equal(report.Commit, loaded.Commit);
        Assert.Equal(3, loaded.Runs.Count);
        Assert.Equal(report.Summary[MetricNames.ExpectedEvidenceCoverage], loaded.Summary[MetricNames.ExpectedEvidenceCoverage]);
        Assert.Equal(report.Cost.Chat, loaded.Cost.Chat);
    }
}
