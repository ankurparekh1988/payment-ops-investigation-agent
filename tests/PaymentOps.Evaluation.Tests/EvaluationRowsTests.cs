using System.Text.Json;
using System.Text.RegularExpressions;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Recording;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Tests.Shared;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;
using static PaymentOps.Evaluation.Tests.EvaluationReportTests;

namespace PaymentOps.Evaluation.Tests;

public sealed class EvaluationRowsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private static EvaluationReport Investigations(params IReadOnlyDictionary<string, JudgeScore>[] judged)
    {
        RunOutcome[] runs = [Run(), Run(), Run()];
        var context = judged.Length == 0 ? Context with { Judge = null, JudgePromptVersion = null } : Context;
        IReadOnlyList<IReadOnlyDictionary<string, JudgeScore>> scores = judged.Length == 0 ? [.. runs.Select(_ => new Dictionary<string, JudgeScore>())] : judged;
        var report = EvaluationReport.ForInvestigations(context, EvaluationReportTests.Cases, runs, scores, Prices(), null, null);
        return report with { Gate = report.Gate with { Thresholds = new Dictionary<string, double> { [MetricNames.ValidatedResultRate] = 0.91 } } };
    }

    private static EvaluationReport Retrieval() => EvaluationReport.ForRetrieval(
        Context with { System = EvaluationReport.RetrievalSystem, Prompts = [], Chat = null, Judge = null, JudgePromptVersion = null },
        new RetrievalResult(
            new RunMetrics(
                new Dictionary<string, double?> { [MetricNames.RetrievalRecallAt5] = 0.8, [MetricNames.RetrievalMrr] = 0.7 },
                new HardGates([], []),
                new Dictionary<string, IReadOnlyDictionary<string, bool>>()),
            10,
            []),
        null,
        null);

    private static IReadOnlyList<EvaluationRow> Rows(EvaluationReport investigations) =>
        EvaluationRows.From(investigations, Retrieval(), "123-1", "schedule", At);

    private static Dictionary<string, JudgeScore> Scored(params int?[] groundedness) => groundedness
        .Select((g, i) => (Key: $"case-{i}", Score: g is { } v ? new JudgeScore(v, v, v, JudgeScore.Scored, null) : JudgeScore.Skipped("not creditable")))
        .ToDictionary(e => e.Key, e => e.Score, StringComparer.Ordinal);

    [Fact]
    public void Each_run_and_the_summary_get_a_row_per_metric()
    {
        var investigations = Investigations();
        var rows = Rows(investigations);

        var investigationRows = rows.Where(r => r.System == "agent").ToList();
        Assert.Equal(((3 + 1) * (investigations.Summary.Count + 2)) + 1, investigationRows.Count);
        Assert.Equal(((1 + 1) * (2 + 2)) + 1, rows.Count(r => r.System == EvaluationReport.RetrievalSystem));
        Assert.All(investigationRows.Where(r => r.Metric == MetricNames.ValidatedResultRate), r => Assert.Equal(0.91, r.Threshold));
        Assert.All(rows, r => Assert.Equal("123-1", r.EvaluationId));

        var summary = Assert.Single(investigationRows, r => r.Run == 0 && r.Metric == MetricNames.ValidatedResultRate);
        Assert.Equal(investigations.Summary[MetricNames.ValidatedResultRate].Median, summary.Value);
        Assert.Equal(GateReport.Passed, summary.Verdict);
        Assert.Equal("not-gating", Assert.Single(investigationRows, r => r.Run == 0 && r.Metric == MetricNames.ProposalRecall).Verdict);
        Assert.Equal(GateReport.Passed, Assert.Single(investigationRows, r => r.Run == 2 && r.Metric == MetricNames.AuthorizationLeaks).Verdict);
    }

    [Fact]
    public void A_hard_gate_violation_fails_its_run_and_the_summary()
    {
        var report = EvaluationReport.ForInvestigations(Context with { Judge = null }, EvaluationReportTests.Cases, [Run(), Run(leak: true), Run()],
            [new Dictionary<string, JudgeScore>(), new Dictionary<string, JudgeScore>(), new Dictionary<string, JudgeScore>()], Prices(), null, null);

        var leaks = Rows(report).Where(r => r.System == "agent" && r.Metric == MetricNames.AuthorizationLeaks).ToDictionary(r => r.Run);

        Assert.Equal(GateReport.Failed, leaks[2].Verdict);
        Assert.Equal(1, leaks[2].Value);
        Assert.Equal(GateReport.Passed, leaks[1].Verdict);
        Assert.Equal(GateReport.Failed, leaks[0].Verdict);
    }

    [Fact]
    public void The_gate_verdict_is_its_own_summary_row()
    {
        var failed = Investigations() with { Gate = Investigations().Gate with { Verdict = GateReport.Failed, Failures = ["run 2: viability"] } };

        var gate = Assert.Single(Rows(failed), r => r.System == "agent" && r.Metric == EvaluationRows.GateMetric);

        Assert.Equal((0, GateReport.Failed, 1.0), (gate.Run, gate.Verdict, gate.Value!.Value));
        Assert.Equal(GateReport.ReportOnly, Assert.Single(Rows(Investigations()), r => r.System == "agent" && r.Metric == EvaluationRows.GateMetric).Verdict);
        Assert.Single(Rows(Investigations()), r => r.System == EvaluationReport.RetrievalSystem && r.Metric == EvaluationRows.GateMetric);
    }

    [Fact]
    public void An_na_metric_is_recorded_as_null_not_zero()
    {
        var rows = Rows(Investigations()).Where(r => r.System == "agent" && r.Metric == MetricNames.ToolArgumentAccuracy).ToList();

        Assert.Equal(4, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Null(r.Value);
            Assert.Equal(0, r.SampleSize);
        });
    }

    [Fact]
    public void Judge_scores_are_recorded_per_run_and_pooled()
    {
        var report = Investigations(Scored(4, 5), Scored(3), Scored(5, 5));

        var rows = Rows(report).Where(r => r.Metric.StartsWith("judge_", StringComparison.Ordinal)).ToList();

        Assert.Equal(
            ["judge_groundedness", "judge_relevance", "judge_root_cause_agreement"],
            rows.Select(r => r.Metric).Distinct().Order(StringComparer.Ordinal));
        var groundedness = rows.Where(r => r.Metric == "judge_groundedness").ToDictionary(r => r.Run);
        Assert.Equal((4.5, 2), (groundedness[1].Value!.Value, groundedness[1].SampleSize));
        Assert.Equal((3.0, 1), (groundedness[2].Value!.Value, groundedness[2].SampleSize));
        Assert.Equal((5.0, 2), (groundedness[3].Value!.Value, groundedness[3].SampleSize));
        Assert.Equal((5.0, 5), (groundedness[0].Value!.Value, groundedness[0].SampleSize));
        Assert.All(rows, r =>
        {
            Assert.Null(r.Threshold);
            Assert.Equal(EvaluationRows.NotGating, r.Verdict);
        });
        Assert.Contains("| groundedness | 5 |", ReportMarkdown.Investigations(report), StringComparison.Ordinal);
    }

    [Fact]
    public void No_judge_rows_when_the_judge_did_not_run()
    {
        Assert.DoesNotContain(Rows(Investigations()), r => r.Metric.StartsWith("judge_", StringComparison.Ordinal));
    }

    [Fact]
    public void Unscored_judge_entries_are_left_out_not_zeroed()
    {
        var rows = Rows(Investigations(Scored(4, 5, null), Scored(null, null), Scored(4)))
            .Where(r => r.Metric == "judge_groundedness").ToDictionary(r => r.Run);

        Assert.Equal((4.5, 2), (rows[1].Value!.Value, rows[1].SampleSize));
        Assert.False(rows.ContainsKey(2));
        Assert.Equal((4.0, 3), (rows[0].Value!.Value, rows[0].SampleSize));
        Assert.DoesNotContain(rows.Values, r => r.Value == 0);
    }

    [Fact]
    public void Judge_rows_never_change_a_verdict()
    {
        var without = Rows(Investigations()).Where(r => !r.Metric.StartsWith("judge_", StringComparison.Ordinal)).Select(r => (r.Run, r.Metric, r.Verdict));
        var withPoorScores = Rows(Investigations(Scored(1, 1), Scored(1), Scored(1)))
            .Where(r => !r.Metric.StartsWith("judge_", StringComparison.Ordinal)).Select(r => (r.Run, r.Metric, r.Verdict));

        Assert.Equal(without, withPoorScores);
    }

    [Fact]
    public void Rows_carry_no_model_text_or_case_content()
    {
        var json = JsonSerializer.Serialize(Rows(Investigations(Scored(4), Scored(4), Scored(4))));

        Assert.DoesNotContain("MODEL-TEXT-SENTINEL", json, StringComparison.Ordinal);
        Assert.DoesNotContain("reg-1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("held-1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("M-1042", json, StringComparison.Ordinal);
        Assert.Equal(
            typeof(EvaluationRow).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal),
            JsonDocument.Parse(json).RootElement[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_table_columns_are_the_row_fields_with_names_Log_Analytics_accepts()
    {
        var terraform = File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, "infra", "modules", "monitoring", "evaluation.tf"));
        var columns = Regex.Matches(terraform, @"^\s+(?<name>\w+)\s+= ""(?:datetime|string|int|real)""
?$", RegexOptions.Multiline).Select(m => m.Groups["name"].Value).ToList();

        Assert.Equal(typeof(EvaluationRow).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal), columns.Order(StringComparer.Ordinal));

        // Log Analytics refuses a one-character column name such as "N".
        Assert.All(columns, c => Assert.True(c.Length > 1, c));
    }

    [Fact]
    public async Task Reports_from_different_commits_are_refused()
    {
        var sink = new RecordingSink();
        var other = Retrieval() with { Commit = new string('d', 40) };

        var exit = await RecordEvaluationCommand.RunAsync(Args(Temp(Investigations().ToJson()), Temp(other.ToJson())), () => sink);

        Assert.Equal(2, exit);
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public async Task A_fake_sink_receives_every_row_once()
    {
        var sink = new RecordingSink();
        var investigations = Investigations(Scored(4), Scored(4), Scored(4));

        var exit = await RecordEvaluationCommand.RunAsync(Args(Temp(investigations.ToJson()), Temp(Retrieval().ToJson())), () => sink);

        Assert.Equal(0, exit);
        var batch = Assert.Single(sink.Batches);
        Assert.Equal(EvaluationRows.From(investigations, Retrieval(), "42-1", "workflow_dispatch", At).Count, batch.Count);
        Assert.All(batch, r => Assert.Equal(("42-1", "workflow_dispatch"), (r.EvaluationId, r.Trigger)));
    }

    [Fact]
    public async Task An_unavailable_sink_exits_3_and_a_pull_request_trigger_exits_2()
    {
        var failing = new RecordingSink { Failure = new CouldNotEvaluateException("Log ingestion was refused (403).") };

        Assert.Equal(3, await RecordEvaluationCommand.RunAsync(Args(Temp(Investigations().ToJson()), Temp(Retrieval().ToJson())), () => failing));
        Assert.Equal(2, await RecordEvaluationCommand.RunAsync(
            [.. Args(Temp(Investigations().ToJson()), Temp(Retrieval().ToJson()))[..^2], "--trigger", "pull_request"], () => new RecordingSink()));
    }

    private static string[] Args(string investigations, string retrieval) =>
    [
        "--investigations", investigations, "--retrieval", retrieval, "--ingestion-endpoint", "https://dce.invalid",
        "--rule", "dcr-00000000000000000000000000000000", "--evaluation-id", "42-1", "--trigger", "workflow_dispatch",
    ];

    private sealed class RecordingSink : IEvaluationSink
    {
        public List<IReadOnlyList<EvaluationRow>> Batches { get; } = [];

        public Exception? Failure { get; init; }

        public Task WriteAsync(IReadOnlyList<EvaluationRow> rows, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Batches.Add(rows);
            return Task.CompletedTask;
        }
    }
}
