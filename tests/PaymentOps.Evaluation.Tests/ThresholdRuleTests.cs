using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Tests;

public sealed class ThresholdRuleTests
{
    [Fact]
    public void Worst_run_threshold_is_the_worst_run_less_one_item_rounded_down() =>
        Assert.Equal(0.94, ThresholdRule.FromWorstRun(MetricNames.RequiredToolRecall, [1.0, 34 / 35.0, 1.0], 35));

    [Fact]
    public void Lower_is_better_worst_run_threshold_is_the_worst_run_plus_one_item_rounded_up() =>
        Assert.Equal(0.40, ThresholdRule.FromWorstRun(MetricNames.ProposalFalsePositiveRate, [0, 0.2, 0], 5));

    [Fact]
    public void Rounding_is_exact_at_two_decimals()
    {
        Assert.Equal(0.95, ThresholdRule.FromWorstRun(MetricNames.RequiredToolRecall, [0.96, 0.96, 0.97], 100));
        Assert.Equal(0.30, ThresholdRule.FromWorstRun(MetricNames.ProposalFalsePositiveRate, [0.2, 0.1, 0.2], 10));
    }

    [Fact]
    public void A_higher_is_better_threshold_below_zero_clamps_to_zero() =>
        Assert.Equal(0, ThresholdRule.FromWorstRun(MetricNames.RequiredToolRecall, [0, 0, 0], 3));

    [Fact]
    public void A_lower_is_better_threshold_above_one_clamps_to_one() =>
        Assert.Equal(1, ThresholdRule.FromWorstRun(MetricNames.ProposalFalsePositiveRate, [1, 1, 1], 2));

    [Fact]
    public void A_metric_with_N_zero_has_no_threshold() =>
        Assert.Null(ThresholdRule.FromWorstRun(MetricNames.ToolArgumentAccuracy, [], 0));

    // Mean 0.70, sample SD 0.0283: 0.70 - 1.6 x 0.0283 = 0.6547.
    [Fact]
    public void Spread_threshold_is_the_mean_less_1_6_standard_deviations_rounded_down() =>
        Assert.Equal(0.65, ThresholdRule.FromSpread(MetricNames.ToolArgumentAccuracy, [0.70, 0.74, 0.66, 0.70, 0.70], 100));

    // Mean 0.05, sample SD 0.0577: 0.05 + 1.6 x 0.0577 = 0.1424.
    [Fact]
    public void Lower_is_better_spread_threshold_is_the_mean_plus_1_6_standard_deviations_rounded_up() =>
        Assert.Equal(0.15, ThresholdRule.FromSpread(MetricNames.ProposalFalsePositiveRate, [0, 0.1, 0, 0.1], 10));

    [Fact]
    public void A_baseline_that_never_varied_still_allows_one_item()
    {
        Assert.Equal(0.85, ThresholdRule.FromSpread(MetricNames.RequiredToolRecall, [0.9, 0.9, 0.9, 0.9], 20));
        Assert.Equal(0.05, ThresholdRule.FromSpread(MetricNames.ProposalFalsePositiveRate, [0, 0, 0, 0], 23));
    }

    [Fact]
    public void Spread_thresholds_clamp_to_zero_and_one()
    {
        Assert.Equal(0, ThresholdRule.FromSpread(MetricNames.RequiredToolRecall, [0, 0.5, 0, 0.5], 2));
        Assert.Equal(1, ThresholdRule.FromSpread(MetricNames.ProposalFalsePositiveRate, [1, 0.5, 1, 0.5], 2));
    }

    [Fact]
    public void A_spread_needs_at_least_two_runs_and_a_metric_with_N_zero_has_none()
    {
        Assert.Throws<ArgumentException>(() => ThresholdRule.FromSpread(MetricNames.RequiredToolRecall, [0.9], 10));
        Assert.Null(ThresholdRule.FromSpread(MetricNames.ToolArgumentAccuracy, [], 0));
    }

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
            new Dictionary<string, ThresholdEntry> { [MetricNames.RequiredToolRecall] = new(0.94, ThresholdRule.Higher, 35, 0.97, 1, 1, 0.99, 0.02, [0.97, 1, 1]) });
        var path = Path.GetTempFileName();
        File.WriteAllText(path, thresholds.ToJson());

        var loaded = Thresholds.Load(path);

        Assert.Equal(thresholds.BaselineCommit, loaded.BaselineCommit);
        Assert.Equivalent(thresholds.Metrics[MetricNames.RequiredToolRecall], loaded.Metrics[MetricNames.RequiredToolRecall], strict: true);
        Assert.Contains("\"baseline_commit\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("\"baseline_standard_deviation\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_thresholds_file_is_invalid_data()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "schema_version": 2, "baseline_commit": "x", "rule": "r", "metrics": {}, "extra": 1 }""");

        Assert.Throws<InvalidDataException>(() => Thresholds.Load(path));
    }

    [Fact]
    public void A_thresholds_file_from_the_worst_run_rule_is_refused()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "schema_version": 1, "baseline_commit": "x", "rule": "r", "metrics": {} }""");

        Assert.Throws<InvalidDataException>(() => Thresholds.Load(path));
    }
}
