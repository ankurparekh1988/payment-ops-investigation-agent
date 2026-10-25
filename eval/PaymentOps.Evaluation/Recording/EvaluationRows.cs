using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Recording;

/// <summary>
/// One metric of one run of a recorded evaluation, as a row of the AiEvaluation_CL table. Names, numbers and
/// versions only: never model text, case content, endpoints or ids.
/// </summary>
/// <param name="Run">1 to 3 for each run; 0 for the summary, whose value is the median and whose verdict is the gate's.</param>
/// <param name="Value">Null when the labels give the metric no opportunities, never 0.</param>
/// <param name="Verdict"><c>passed</c>, <c>failed</c>, <c>report-only</c> or <c>not-gating</c>.</param>
internal sealed record EvaluationRow(
    DateTimeOffset TimeGenerated,
    string EvaluationId,
    string Trigger,
    string Commit,
    string System,
    string Prompt,
    string ChatModel,
    string KnowledgeIndex,
    int Run,
    string Metric,
    double? Value,
    double? Threshold,
    int N,
    string Direction,
    string Verdict);

/// <summary>Turns an evaluation's two reports into history rows: every metric of every run, the summary, the hard gates and the judge.</summary>
internal static class EvaluationRows
{
    public const string NotGating = "not-gating";

    private static readonly (string Metric, Func<HardGates, int> Count)[] HardGateCounts =
    [
        (MetricNames.AuthorizationLeaks, h => h.AuthorizationLeaks.Count),
        (MetricNames.UnauthorizedStateChanges, h => h.UnauthorizedStateChanges.Count),
    ];

    public static IReadOnlyList<EvaluationRow> From(EvaluationReport investigations, EvaluationReport retrieval, string evaluationId, string trigger, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(investigations);
        ArgumentNullException.ThrowIfNull(retrieval);
        if (investigations.Commit != retrieval.Commit)
        {
            throw new InvalidDataException($"The reports are from different commits ({investigations.Commit} and {retrieval.Commit}); one evaluation is one commit.");
        }

        return [.. Rows(investigations, evaluationId, trigger, at), .. Rows(retrieval, evaluationId, trigger, at)];
    }

    private static IEnumerable<EvaluationRow> Rows(EvaluationReport report, string evaluationId, string trigger, DateTimeOffset at)
    {
        var prompt = string.Join(",", report.Prompts.Select(p => $"{p.Id}@{p.Version}"));
        var chat = report.Chat is { } c ? $"{c.Deployment}:{c.Model}:{c.Version}" : string.Empty;
        EvaluationRow Row(int run, string metric, double? value, double? threshold, int n, string direction, string verdict) =>
            new(at, evaluationId, trigger, report.Commit, report.System, prompt, chat, report.KnowledgeIndex, run, metric, value, threshold, n, direction, verdict);

        foreach (var (metric, summary) in report.Summary.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var direction = ThresholdRule.Direction(metric);
            double? threshold = report.Gate.Thresholds.TryGetValue(metric, out var t) ? t : null;
            foreach (var run in report.Runs)
            {
                var value = run.Metrics.GetValueOrDefault(metric);
                yield return Row(run.Run, metric, value, threshold, summary.N, direction, Verdict(metric, value, threshold, report.Gate.Verdict));
            }

            yield return Row(0, metric, summary.Median, threshold, summary.N, direction, Verdict(metric, summary.Median, threshold, report.Gate.Verdict));
        }

        // Every run must pass the hard gates, so each run's count has its own verdict.
        var cases = report.Runs.Select(r => report.System == EvaluationReport.RetrievalSystem ? report.RetrievalCases.Count : r.Cases.Count).ToList();
        foreach (var (metric, count) in HardGateCounts)
        {
            var counts = report.Runs.Select(r => count(r.HardGates)).ToList();
            for (var i = 0; i < report.Runs.Count; i++)
            {
                yield return Row(report.Runs[i].Run, metric, counts[i], 0, cases[i], ThresholdRule.Lower, HardVerdict(counts[i]));
            }

            yield return Row(0, metric, counts.Sum(), 0, cases.Sum(), ThresholdRule.Lower, HardVerdict(counts.Sum()));
        }

        // Recorded, not gating; the summary pools every run's scored entries, as the report's judge table does.
        if (report.Judge is not null)
        {
            foreach (var (metric, _, score) in JudgeScore.Dimensions)
            {
                foreach (var run in report.Runs)
                {
                    if (JudgeScore.Median(run.Judge.Values, score) is var (median, n))
                    {
                        yield return Row(run.Run, metric, median, null, n, ThresholdRule.Higher, NotGating);
                    }
                }

                if (JudgeScore.Median(report.Runs.SelectMany(r => r.Judge.Values), score) is var (pooled, pooledN))
                {
                    yield return Row(0, metric, pooled, null, pooledN, ThresholdRule.Higher, NotGating);
                }
            }
        }
    }

    // Only gating metrics with a measured value can pass or fail; the gate decides on the summary row.
    private static string Verdict(string metric, double? value, double? threshold, string gateVerdict) =>
        !ThresholdRule.Gating.Contains(metric) || value is null ? NotGating
        : threshold is { } t ? (ThresholdRule.Meets(metric, value.Value, t) ? GateReport.Passed : GateReport.Failed)
        : gateVerdict == GateReport.ReportOnly ? GateReport.ReportOnly
        : NotGating;

    private static string HardVerdict(int count) => count == 0 ? GateReport.Passed : GateReport.Failed;
}
