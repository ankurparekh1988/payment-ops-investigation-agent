using System.Globalization;
using System.Text;
using System.Text.Json;

using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Commands;

/// <summary>
/// routing-decision: ADR 0023's rule over a mini-only control report, a cascade report and the cascade's safety report.
/// Exit 0 adopt, 1 reject, 2 invalid or inconclusive; only adopt lets a later change turn routing on.
/// </summary>
internal static class RoutingDecisionCommand
{
    public const string Usage =
        "routing-decision --control <report.json> --cascade <report.json> --cascade-safety <report.json> --thresholds <file> --out <file> --summary <file>";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    public static Task<int> RunAsync(string[] args) => ExitCodes.GuardAsync("Routing decision", Usage, async () =>
    {
        var options = Options.Parse(args, ["control", "cascade", "cascade-safety", "thresholds", "out", "summary"], []);
        var (outPath, summaryPath) = (options.OutputFile("out"), options.OutputFile("summary"));
        var control = EvaluationReport.Load(options.One("control"));
        var cascade = EvaluationReport.Load(options.One("cascade"));
        var safety = EvaluationReport.Load(options.One("cascade-safety"));
        var thresholds = Thresholds.Load(options.One("thresholds"));

        var result = RoutingDecision.Decide(control, cascade, safety, thresholds);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(
            new
            {
                result.Verdict,
                RoutingDecision.MinCostReduction,
                RoutingDecision.MaxLatencyRatio,
                Control = Side(control),
                Cascade = Side(cascade),
                SafetyCommit = safety.Source?.Commit,
                result.Checks,
            },
            Json));
        await File.AppendAllTextAsync(summaryPath, Summary(result, control, cascade, thresholds));
        Console.WriteLine($"Routing decision: {result.Verdict}.");
        return result.ExitCode;
    });

    private static object Side(EvaluationReport report) => new
    {
        report.Source?.Commit,
        report.Source?.Tree,
        report.Inputs?.Routing,
        report.Cost.AllInPerValidatedInvestigation,
        report.Cost.Currency,
        report.Cost.AllInStatus,
        LatencyP95Ms = report.Latency.P95,
        Escalations = Escalations(report),
    };

    private static (int Escalated, int Cases, int ReusedReads) Escalations(EvaluationReport report)
    {
        var results = report.Runs.SelectMany(r => r.Cases).ToList();
        return (results.Count(r => r.Escalation == "escalated"), results.Count, results.Sum(r => r.ReusedReads));
    }

    internal static string Summary(RoutingDecision.Result result, EvaluationReport control, EvaluationReport cascade, Thresholds thresholds)
    {
        var md = new StringBuilder();
        md.AppendLine("## Routing decision (ADR 0023)").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Verdict: **{result.Verdict}**. Control {control.Source?.Commit ?? "unknown"}, cascade {cascade.Source?.Commit ?? "unknown"}, tree {cascade.Source?.Tree ?? "unknown"}.").AppendLine();
        md.AppendLine("| Step | Held | Findings |").AppendLine("|---|---|---|");
        foreach (var check in result.Checks)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {check.Step} | {(check.Passed ? "yes" : "no")} | {string.Join("<br>", check.Findings.DefaultIfEmpty("—"))} |");
        }

        var runs = cascade.Runs.Count;
        md.AppendLine().AppendLine("| Metric | Threshold | Margin | Control median | Cascade median | Cascade runs |").AppendLine("|---|---|---|---|---|---|");
        foreach (var (metric, entry) in thresholds.Metrics.Where(m => ThresholdRule.Gating.Contains(m.Key)).OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (RoutingDecision.Median(cascade, metric) is not { } median)
            {
                continue;
            }

            var perRun = string.Join(", ", cascade.Runs.Select(r => r.Metrics.GetValueOrDefault(metric) is { } v ? v.ToString("F3", CultureInfo.InvariantCulture) : "—"));
            md.AppendLine(CultureInfo.InvariantCulture, $"| {metric} | {entry.Threshold:F2} | {RoutingDecision.Margin(entry):F3} | {RoutingDecision.Median(control, metric):F3} | {median:F3} | {perRun} |");
        }

        var (escalated, cases, reused) = Escalations(cascade);
        md.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Escalated {escalated} of {cases} cascade investigations ({(cases == 0 ? 0 : (double)escalated / cases):P0}); {reused} reads reused.");
        md.AppendLine(CultureInfo.InvariantCulture, $"Sample: {cascade.Stability.Count} cases × {runs} runs on each side. A small sample: a difference inside the margin isn't evidence either way.");
        return md.ToString();
    }
}
