namespace PaymentOps.Evaluation.Scoring;

/// <summary>A metric across runs. All null only when the labels give it no opportunities (N = 0).</summary>
internal sealed record MetricSummary(double? Median, double? Min, double? Max, int N);

internal static class Aggregation
{
    public static IReadOnlyDictionary<string, MetricSummary> Summarize(IReadOnlyList<RunMetrics> runs, IReadOnlyDictionary<string, int> n)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(n);

        return n.ToDictionary(
            entry => entry.Key,
            entry =>
            {
                var values = runs.Select(run => run.Values.GetValueOrDefault(entry.Key)).OfType<double>().Order().ToList();
                return values.Count == 0 || entry.Value == 0
                    ? new MetricSummary(null, null, null, entry.Value)
                    : new MetricSummary(Median(values), values[0], values[^1], entry.Value);
            },
            StringComparer.Ordinal);
    }

    public static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
}
