using System.Globalization;
using System.Text;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Reporting;

/// <summary>The job summary for a report: the verdict first, then the evidence behind it. Never any model text.</summary>
internal static class ReportMarkdown
{
    public static string Investigations(EvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"## Investigation evaluation: {report.System}").AppendLine();
        Header(md, report);
        Metrics(md, report, MetricNames.Investigation);

        md.AppendLine("### Regression and held-out").AppendLine();
        md.AppendLine("| Metric | Regression | Held-out |").AppendLine("|---|---|---|");
        foreach (var metric in MetricNames.Investigation)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {metric} | {Set(report, EvaluationReport.RegressionSet, metric)} | {Set(report, EvaluationReport.HeldOutSet, metric)} |");
        }

        var groups = report.Breakdown.Keys.Where(k => k.StartsWith("group:", StringComparison.Ordinal)).ToList();
        md.AppendLine().AppendLine("### By group (median)").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"| Metric | {string.Join(" | ", groups.Select(g => g["group:".Length..]))} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"|---|{string.Concat(groups.Select(_ => "---|"))}");
        foreach (var metric in MetricNames.Investigation)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {metric} | {string.Join(" | ", groups.Select(g => Set(report, g, metric)))} |");
        }

        md.AppendLine().AppendLine("### Stability").AppendLine();
        md.AppendLine("Each run's outcome, and the eligible metrics the case missed in that run.").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"| Case | Set | {string.Join(" | ", report.Runs.Select(r => $"Run {r.Run}"))} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"|---|---|{string.Concat(report.Runs.Select(_ => "---|"))}");
        foreach (var c in report.Stability)
        {
            var cells = c.Outcomes.Select((outcome, run) =>
            {
                var missed = c.Metrics.Where(m => !m.Value[run]).Select(m => m.Key).ToList();
                return missed.Count == 0 ? outcome : $"{outcome}; missed {string.Join(", ", missed)}";
            });
            md.AppendLine(CultureInfo.InvariantCulture, $"| {c.CaseId} | {c.Set} | {string.Join(" | ", cells)} |");
        }

        md.AppendLine().AppendLine("### Judge (recorded, not gating)").AppendLine();
        var scores = report.Runs.SelectMany(r => r.Judge.Values).ToList();
        if (report.Judge is null)
        {
            md.AppendLine("The judge didn't run.");
        }
        else
        {
            var scored = scores.Where(s => s.Status == JudgeScore.Scored).ToList();
            md.AppendLine(CultureInfo.InvariantCulture, $"{report.Judge.Deployment} ({report.Judge.Model} {report.Judge.Version}), prompt {report.JudgePromptVersion}; {scored.Count} scored, {scores.Count - scored.Count} not scored.").AppendLine();
            md.AppendLine("| Dimension | Median |").AppendLine("|---|---|");
            md.AppendLine(CultureInfo.InvariantCulture, $"| groundedness | {Median(scored.Select(s => s.Groundedness!.Value))} |");
            md.AppendLine(CultureInfo.InvariantCulture, $"| relevance | {Median(scored.Select(s => s.Relevance!.Value))} |");
            md.AppendLine(CultureInfo.InvariantCulture, $"| root-cause agreement | {Median(scored.Select(s => s.RootCauseAgreement!.Value))} |");
        }

        md.AppendLine().AppendLine("### Latency and cost").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Latency p50 {Seconds(report.Latency.P50)}, p95 {Seconds(report.Latency.P95)}; time to first progress p50 {Seconds(report.TimeToFirstProgress.P50)}, p95 {Seconds(report.TimeToFirstProgress.P95)}.").AppendLine();
        var cost = report.Cost;
        md.AppendLine(CultureInfo.InvariantCulture, $"Estimated model cost per validated investigation: {Money(cost.PerValidatedInvestigation, cost.Currency)} ({cost.ValidatedResults} validated results; chat {Money(cost.Chat, cost.Currency)}, judge {Money(cost.Judge, cost.Currency)}). Informational; prices from {cost.PriceSource ?? "no price list entry"}.");
        md.AppendLine(CultureInfo.InvariantCulture, $"Chat tokens: {cost.ChatTokens.Input} in ({cost.ChatTokens.CachedInput} cached), {cost.ChatTokens.Output} out.");
        return md.ToString();
    }

    public static string Retrieval(EvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var md = new StringBuilder();
        md.AppendLine("## Retrieval evaluation").AppendLine();
        Header(md, report);
        Metrics(md, report, [MetricNames.RetrievalRecallAt5, MetricNames.RetrievalMrr]);
        md.AppendLine("| Case | Kind | Expected rank | Top 5 |").AppendLine("|---|---|---|---|");
        foreach (var run in report.RetrievalCases)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {run.CaseId} | {run.Kind} | {run.ExpectedRank?.ToString(CultureInfo.InvariantCulture) ?? "-"} | {string.Join("<br>", run.Top.Select(h => $"{h.DocId}: {h.SectionPath} ({h.Classification})"))} |");
        }

        return md.ToString();
    }

    private static void Header(StringBuilder md, EvaluationReport report)
    {
        var gate = report.Gate;
        md.AppendLine(gate.Verdict switch
        {
            GateReport.Passed => "Gate: **passed**.",
            GateReport.ReportOnly => "Gate: **report-only** (no thresholds); hard gates passed.",
            _ => "Gate: **FAILED**.",
        }).AppendLine();
        foreach (var failure in gate.Failures)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- {failure}");
        }

        md.AppendLine(CultureInfo.InvariantCulture, $"Commit `{report.Commit}`; prompts {string.Join(", ", report.Prompts.Select(p => $"{p.Id} {p.Version}"))}; chat {report.Chat?.Deployment ?? "none"} ({report.Chat?.Model} {report.Chat?.Version}); started {report.StartedAt:yyyy-MM-dd HH:mm} UTC; {report.Runs.Count} run(s).").AppendLine();

        md.AppendLine("### Hard gates").AppendLine();
        var leaks = report.Runs.Sum(r => r.HardGates.AuthorizationLeaks.Count);
        var changes = report.Runs.Sum(r => r.HardGates.UnauthorizedStateChanges.Count);
        md.AppendLine(CultureInfo.InvariantCulture, $"- {MetricNames.AuthorizationLeaks}: {leaks} across every run");
        md.AppendLine(CultureInfo.InvariantCulture, $"- {MetricNames.UnauthorizedStateChanges}: {changes} across every run").AppendLine();
    }

    private static void Metrics(StringBuilder md, EvaluationReport report, IEnumerable<string> names)
    {
        md.AppendLine("### Metrics").AppendLine();
        md.AppendLine("| Metric | Median | Min | Max | Threshold | N |").AppendLine("|---|---|---|---|---|---|");
        foreach (var metric in names)
        {
            var s = report.Summary.GetValueOrDefault(metric) ?? new MetricSummary(null, null, null, 0);
            var threshold = report.Gate.Thresholds.TryGetValue(metric, out var t) ? $"{t:F2} ({ThresholdRule.Direction(metric)})"
                : ThresholdRule.Gating.Contains(metric) ? "none" : "not gating";
            md.AppendLine(CultureInfo.InvariantCulture, $"| {metric} | {Value(s.Median)} | {Value(s.Min)} | {Value(s.Max)} | {threshold} | {s.N} |");
        }

        md.AppendLine();
    }

    private static string Set(EvaluationReport report, string set, string metric) =>
        report.Breakdown.TryGetValue(set, out var summary) ? Value(summary.GetValueOrDefault(metric)?.Median) : "-";

    private static string Value(double? value) => value is { } v ? v.ToString("F3", CultureInfo.InvariantCulture) : "n/a";

    private static string Median(IEnumerable<int> scores)
    {
        var sorted = scores.Select(s => (double)s).Order().ToList();
        return sorted.Count == 0 ? "n/a" : Aggregation.Median(sorted).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string Seconds(long? ms) => ms is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v / 1000.0:0.0} s") : "n/a";

    private static string Money(decimal? amount, string? currency) =>
        amount is { } a ? string.Create(CultureInfo.InvariantCulture, $"{a:0.0000} {currency}") : "not priced";
}
