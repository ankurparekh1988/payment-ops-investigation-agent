using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Tests;

public sealed class GateTests
{
    internal static readonly Dictionary<string, int> N = new(StringComparer.Ordinal)
    {
        [MetricNames.RequiredToolRecall] = 10,
        [MetricNames.ProposalFalsePositiveRate] = 5,
        [MetricNames.ToolArgumentAccuracy] = 0,
    };

    private static readonly HashSet<string> Applicable = [MetricNames.RequiredToolRecall, MetricNames.ProposalFalsePositiveRate];

    internal static RunMetrics Run(double recall, double falsePositives = 0, bool leak = false) => new(
        new Dictionary<string, double?>
        {
            [MetricNames.RequiredToolRecall] = recall,
            [MetricNames.ProposalFalsePositiveRate] = falsePositives,
            [MetricNames.ToolArgumentAccuracy] = null,
        },
        new HardGates(leak ? [new Violation("a", "retrieved Restricted section kr")] : [], []),
        new Dictionary<string, IReadOnlyDictionary<string, bool>>());

    private static Thresholds Thresholds(params (string Metric, double Value)[] entries) => new(
        Scoring.Thresholds.SupportedSchemaVersion,
        new string('a', 40),
        ThresholdRule.Text,
        entries.ToDictionary(e => e.Metric, e => new ThresholdEntry(e.Value, ThresholdRule.Direction(e.Metric), N.GetValueOrDefault(e.Metric, 30), 0, 0, 0)));

    private static GateVerdict Evaluate(IReadOnlyList<RunMetrics> runs, Thresholds? thresholds) =>
        Gate.Evaluate(runs, Aggregation.Summarize(runs, N), thresholds, Applicable);

    [Fact]
    public void A_hard_gate_failure_in_any_run_fails_the_gate()
    {
        var verdict = Evaluate([Run(1), Run(1, leak: true), Run(1)], null);

        Assert.False(verdict.HardPassed);
        Assert.False(verdict.Passed);
        Assert.Contains(verdict.Failures, f => f.Contains("authorization_leaks", StringComparison.Ordinal));
    }

    [Fact]
    public void A_soft_metric_passes_at_the_threshold_and_fails_below_it()
    {
        var thresholds = Thresholds((MetricNames.RequiredToolRecall, 0.94), (MetricNames.ProposalFalsePositiveRate, 0.40));

        Assert.True(Evaluate([Run(0.94, 0.40), Run(0.94, 0.40), Run(0.94, 0.40)], thresholds).Passed);
        Assert.False(Evaluate([Run(0.93), Run(0.93), Run(0.93)], thresholds).Passed);
        Assert.False(Evaluate([Run(1, 0.41), Run(1, 0.41), Run(1, 0.41)], thresholds).Passed);
    }

    [Fact]
    public void Without_thresholds_only_hard_gates_decide()
    {
        var verdict = Evaluate([Run(0), Run(0), Run(0)], null);

        Assert.True(verdict.Passed);
        Assert.True(verdict.ReportOnly);
    }

    [Fact]
    public void A_missing_threshold_for_an_applicable_metric_is_a_configuration_error()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Evaluate([Run(1)], Thresholds((MetricNames.RequiredToolRecall, 0.9))));

        Assert.Contains(MetricNames.ProposalFalsePositiveRate, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Thresholds_for_metrics_outside_the_command_are_ignored()
    {
        var thresholds = Thresholds((MetricNames.RequiredToolRecall, 0.9), (MetricNames.ProposalFalsePositiveRate, 0.4), (MetricNames.RetrievalRecallAt5, 0.99));

        Assert.True(Evaluate([Run(1)], thresholds).Passed);
    }

    [Fact]
    public void Thresholds_derived_from_different_labels_are_a_configuration_error()
    {
        var thresholds = Thresholds((MetricNames.RequiredToolRecall, 0.9), (MetricNames.ProposalFalsePositiveRate, 0.4)) is var t
            ? t with { Metrics = new Dictionary<string, ThresholdEntry>(t.Metrics) { [MetricNames.RequiredToolRecall] = t.Metrics[MetricNames.RequiredToolRecall] with { N = 11 } } }
            : null;

        var ex = Assert.Throws<InvalidDataException>(() => Evaluate([Run(1)], thresholds));

        Assert.Contains(MetricNames.RequiredToolRecall, ex.Message, StringComparison.Ordinal);
    }
}
