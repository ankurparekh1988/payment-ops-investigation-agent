using System.Text.Json;

using PaymentOps.Evaluation.Cases;

namespace PaymentOps.Evaluation.Scoring;

/// <summary>
/// The pre-registered threshold rule: a change passes if its median run is no worse than the worst baseline run
/// by more than one item. Fixed before any baseline was seen; changing it needs an ADR.
/// </summary>
internal static class ThresholdRule
{
    public const string Higher = "higher";
    public const string Lower = "lower";

    public const string Text = "higher: floor_to_0.01(min_b - 1/N); lower: ceil_to_0.01(max_b + 1/N); clamp to [0,1]; gate on median of 3";

    public static IReadOnlySet<string> LowerIsBetter { get; } = new HashSet<string>(StringComparer.Ordinal) { MetricNames.ProposalFalsePositiveRate };

    public static IReadOnlySet<string> Gating { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        MetricNames.ValidatedResultRate,
        MetricNames.AllowedOutcomeRate,
        MetricNames.RequiredToolRecall,
        MetricNames.ToolArgumentAccuracy,
        MetricNames.ExpectedEvidenceCoverage,
        MetricNames.ExpectedSectionPrecision,
        MetricNames.ExpectedSectionRecall,
        MetricNames.ProposalFalsePositiveRate,
        MetricNames.RetrievalRecallAt5,
    };

    public static string Direction(string metric) => LowerIsBetter.Contains(metric) ? Lower : Higher;

    /// <summary>The threshold from the baseline runs, or null when the labels give the metric no opportunities.</summary>
    public static double? Derive(string metric, IReadOnlyList<double> baselineRuns, int n)
    {
        ArgumentNullException.ThrowIfNull(baselineRuns);
        if (n == 0)
        {
            return null;
        }

        // Decimal, so a value that is exactly two places (0.96 - 0.01) doesn't round to the place below.
        var item = 1m / n;
        var threshold = LowerIsBetter.Contains(metric)
            ? Math.Ceiling(((decimal)baselineRuns.Max() + item) * 100) / 100
            : Math.Floor(((decimal)baselineRuns.Min() - item) * 100) / 100;
        return (double)Math.Clamp(threshold, 0m, 1m);
    }

    /// <summary>Whether a median meets its threshold, compared exactly so a value at the threshold passes.</summary>
    public static bool Meets(string metric, double median, double threshold) =>
        LowerIsBetter.Contains(metric) ? (decimal)median <= (decimal)threshold : (decimal)median >= (decimal)threshold;
}

/// <param name="Direction"><c>higher</c> or <c>lower</c> is better.</param>
internal sealed record ThresholdEntry(double Threshold, string Direction, int N, double BaselineMin, double BaselineMedian, double BaselineMax);

/// <summary>The frozen thresholds, generated from a baseline report by the harness and never edited by hand.</summary>
internal sealed record Thresholds(int SchemaVersion, string BaselineCommit, string Rule, IReadOnlyDictionary<string, ThresholdEntry> Metrics)
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Indented = new(StrictFiles.SnakeCase) { WriteIndented = true };

    public static Thresholds Load(string path)
    {
        var thresholds = StrictFiles.Read<Thresholds>(path, StrictFiles.SnakeCase);
        return thresholds.SchemaVersion == SupportedSchemaVersion ? thresholds
            : throw new InvalidDataException($"{path} is schema version {thresholds.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, Indented);
}
