using System.Globalization;

using PaymentOps.Evaluation.Cases;

namespace PaymentOps.Evaluation.Scoring;

/// <param name="ReportOnly">No thresholds were given, so only the hard gates decided.</param>
internal sealed record GateVerdict(bool HardPassed, IReadOnlyList<string> Failures, bool Passed, bool ReportOnly);

/// <summary>Hard gates must pass in every run; each applicable soft metric's median must meet its frozen threshold.</summary>
internal static class Gate
{
    /// <param name="applicable">The gating metrics this command measures; a threshold for any other metric is ignored.</param>
    public static GateVerdict Evaluate(IReadOnlyList<RunMetrics> runs, IReadOnlyDictionary<string, MetricSummary> summary, Thresholds? thresholds, IReadOnlySet<string> applicable)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(applicable);

        var failures = new List<string>();
        for (var i = 0; i < runs.Count; i++)
        {
            failures.AddRange(runs[i].Hard.AuthorizationLeaks.Select(v => $"run {i + 1}: {MetricNames.AuthorizationLeaks}: {v.CaseId}: {v.Reason}"));
            failures.AddRange(runs[i].Hard.UnauthorizedStateChanges.Select(v => $"run {i + 1}: {MetricNames.UnauthorizedStateChanges}: {v.CaseId}: {v.Reason}"));
        }

        var hardPassed = failures.Count == 0;
        if (thresholds is null)
        {
            return new GateVerdict(hardPassed, failures, hardPassed, ReportOnly: true);
        }

        var measured = applicable.Where(m => summary.GetValueOrDefault(m)?.Median is not null).Order(StringComparer.Ordinal).ToList();
        Require(thresholds, measured);
        foreach (var metric in measured)
        {
            var median = summary[metric].Median!.Value;
            var threshold = thresholds.Metrics[metric].Threshold;
            if (!ThresholdRule.Meets(metric, median, threshold))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{metric}: median {median:F3} against threshold {threshold:F2} ({ThresholdRule.Direction(metric)} is better)"));
            }
        }

        return new GateVerdict(hardPassed, failures, failures.Count == 0, ReportOnly: false);
    }

    /// <summary>
    /// A metric the command can measure must have a threshold; a silent skip would let it regress unseen. Checked
    /// before any run, so a wrong file costs nothing.
    /// </summary>
    public static void Require(Thresholds thresholds, IEnumerable<string> applicable)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var missing = applicable.Where(m => !thresholds.Metrics.ContainsKey(m)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"The thresholds file has no threshold for {string.Join(", ", missing)}.");
        }
    }
}
