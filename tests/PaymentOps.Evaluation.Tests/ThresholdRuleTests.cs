using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Tests;

public sealed class ThresholdRuleTests
{
    [Fact]
    public void Higher_is_better_threshold_is_the_worst_run_less_one_item_rounded_down() =>
        Assert.Equal(0.94, ThresholdRule.Derive(MetricNames.RequiredToolRecall, [1.0, 34 / 35.0, 1.0], 35));

    [Fact]
    public void Lower_is_better_threshold_is_the_worst_run_plus_one_item_rounded_up() =>
        Assert.Equal(0.40, ThresholdRule.Derive(MetricNames.ProposalFalsePositiveRate, [0, 0.2, 0], 5));

    [Fact]
    public void Rounding_is_exact_at_two_decimals()
    {
        Assert.Equal(0.95, ThresholdRule.Derive(MetricNames.RequiredToolRecall, [0.96, 0.96, 0.97], 100));
        Assert.Equal(0.30, ThresholdRule.Derive(MetricNames.ProposalFalsePositiveRate, [0.2, 0.1, 0.2], 10));
    }

    [Fact]
    public void A_higher_is_better_threshold_below_zero_clamps_to_zero() =>
        Assert.Equal(0, ThresholdRule.Derive(MetricNames.RequiredToolRecall, [0, 0, 0], 3));

    [Fact]
    public void A_lower_is_better_threshold_above_one_clamps_to_one() =>
        Assert.Equal(1, ThresholdRule.Derive(MetricNames.ProposalFalsePositiveRate, [1, 1, 1], 2));

    [Fact]
    public void A_metric_with_N_zero_has_no_threshold() =>
        Assert.Null(ThresholdRule.Derive(MetricNames.ToolArgumentAccuracy, [], 0));

    [Fact]
    public void The_median_of_three_is_the_middle_value()
    {
        List<RunMetrics> runs = [GateTests.Run(0.9), GateTests.Run(0.7), GateTests.Run(0.8)];

        var summary = Aggregation.Summarize(runs, GateTests.N)[MetricNames.RequiredToolRecall];

        Assert.Equal(new MetricSummary(0.8, 0.7, 0.9, 10), summary);
    }

    [Fact]
    public void A_single_run_is_its_own_median_min_and_max()
    {
        var summary = Aggregation.Summarize([GateTests.Run(0.6)], GateTests.N)[MetricNames.RequiredToolRecall];

        Assert.Equal(new MetricSummary(0.6, 0.6, 0.6, 10), summary);
    }

    [Fact]
    public void The_gating_metrics_are_the_nine_soft_deterministic_ones()
    {
        Assert.Equal(9, ThresholdRule.Gating.Count);
        Assert.DoesNotContain(MetricNames.ProposalRecall, ThresholdRule.Gating);
        Assert.DoesNotContain(MetricNames.RetrievalMrr, ThresholdRule.Gating);
    }

    [Fact]
    public void Thresholds_round_trip_through_their_file()
    {
        var thresholds = new Thresholds(
            Thresholds.SupportedSchemaVersion,
            new string('a', 40),
            ThresholdRule.Text,
            new Dictionary<string, ThresholdEntry> { [MetricNames.RequiredToolRecall] = new(0.94, ThresholdRule.Higher, 35, 0.97, 1, 1) });
        var path = Path.GetTempFileName();
        File.WriteAllText(path, thresholds.ToJson());

        var loaded = Thresholds.Load(path);

        Assert.Equal(thresholds.BaselineCommit, loaded.BaselineCommit);
        Assert.Equal(thresholds.Metrics[MetricNames.RequiredToolRecall], loaded.Metrics[MetricNames.RequiredToolRecall]);
        Assert.Contains("\"baseline_commit\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_thresholds_file_is_invalid_data()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "schema_version": 1, "baseline_commit": "x", "rule": "r", "metrics": {}, "extra": 1 }""");

        Assert.Throws<InvalidDataException>(() => Thresholds.Load(path));
    }
}
