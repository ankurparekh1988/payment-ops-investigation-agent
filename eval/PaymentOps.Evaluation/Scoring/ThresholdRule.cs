using System.Text.Json;

using PaymentOps.Evaluation.Cases;

namespace PaymentOps.Evaluation.Scoring;

/// <summary>
/// The pre-registered threshold rules. Investigation metrics sit 1.6 standard deviations of the baseline's runs from
/// their mean, never less than one item (ADR 0018); retrieval, a single deterministic run, sits one item below it
/// (ADR 0012). Fixed before the baseline was seen; changing them needs an ADR.
/// </summary>
internal static class ThresholdRule
{
    public const string Higher = "higher";
    public const string Lower = "lower";

    public const string Text = "investigations, over 10 baseline runs: higher: floor_to_0.01(mean_b - max(1.6*sd_b, 1/N)); lower: ceil_to_0.01(mean_b + max(1.6*sd_b, 1/N)); retrieval, over 1 run: floor_to_0.01(min_b - 1/N); clamp to [0,1]; gate on median of 3";

    // The median of three runs varies about 0.67 as much as one run; 1.6 SD is 2.33 of those, about 1% per metric.
    private const decimal SpreadMultiple = 1.6m;

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

    /// <summary>One item past the worst baseline run, or null when the labels give the metric no opportunities.</summary>
    public static double? FromWorstRun(string metric, IReadOnlyList<double> baselineRuns, int n)
    {
        ArgumentNullException.ThrowIfNull(baselineRuns);
        if (n == 0)
        {
            return null;
        }

        var item = 1m / n;
        var lowerIsBetter = LowerIsBetter.Contains(metric);
        return Round(lowerIsBetter, lowerIsBetter ? (decimal)baselineRuns.Max() + item : (decimal)baselineRuns.Min() - item);
    }

    /// <summary>The baseline's mean, less (or plus) its margin, or null when the labels give the metric no opportunities.</summary>
    public static double? FromSpread(string metric, IReadOnlyList<double> baselineRuns, int n) =>
        FromSpread(LowerIsBetter.Contains(metric), baselineRuns, n);

    /// <summary>The spread rule for a metric whose direction its own rule decides, such as a safety metric.</summary>
    public static double? FromSpread(bool lowerIsBetter, IReadOnlyList<double> baselineRuns, int n)
    {
        ArgumentNullException.ThrowIfNull(baselineRuns);
        if (n == 0)
        {
            return null;
        }

        var mean = (decimal)baselineRuns.Average();
        var margin = Math.Max(SpreadMultiple * (decimal)StandardDeviation(baselineRuns), 1m / n);
        return Round(lowerIsBetter, lowerIsBetter ? mean + margin : mean - margin);
    }

    /// <summary>The sample standard deviation of the baseline's runs.</summary>
    public static double StandardDeviation(IReadOnlyList<double> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count < 2)
        {
            throw new ArgumentException("A spread needs at least two runs.", nameof(runs));
        }

        var mean = runs.Average();
        return Math.Sqrt(runs.Sum(r => (r - mean) * (r - mean)) / (runs.Count - 1));
    }

    // Decimal, so a value that is exactly two places (0.96 - 0.01) doesn't round to the place below.
    private static double Round(bool lowerIsBetter, decimal value)
    {
        var rounded = lowerIsBetter ? Math.Ceiling(value * 100) / 100 : Math.Floor(value * 100) / 100;
        return (double)Math.Clamp(rounded, 0m, 1m);
    }

    /// <summary>Whether a median meets its threshold, compared exactly so a value at the threshold passes.</summary>
    public static bool Meets(string metric, double median, double threshold) =>
        LowerIsBetter.Contains(metric) ? (decimal)median <= (decimal)threshold : (decimal)median >= (decimal)threshold;
}

/// <param name="Direction"><c>higher</c> or <c>lower</c> is better.</param>
/// <param name="BaselineStandardDeviation">Null for a metric from a single run.</param>
internal sealed record ThresholdEntry(
    double Threshold,
    string Direction,
    int N,
    double BaselineMin,
    double BaselineMedian,
    double BaselineMax,
    double? BaselineMean,
    double? BaselineStandardDeviation,
    IReadOnlyList<double> BaselineRuns);

/// <summary>The frozen thresholds, generated from a baseline report by the harness and never edited by hand.</summary>
internal sealed record Thresholds(int SchemaVersion, string BaselineCommit, string Rule, IReadOnlyDictionary<string, ThresholdEntry> Metrics)
{
    public const int SupportedSchemaVersion = 2;

    private static readonly JsonSerializerOptions Indented = new(StrictFiles.SnakeCase) { WriteIndented = true };

    public static Thresholds Load(string path)
    {
        var thresholds = StrictFiles.Read<Thresholds>(path, StrictFiles.SnakeCase);
        return thresholds.SchemaVersion == SupportedSchemaVersion ? thresholds
            : throw new InvalidDataException($"{path} is schema version {thresholds.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, Indented);
}
