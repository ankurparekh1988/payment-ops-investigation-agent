using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Challenger;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Tests;

public sealed class KillTestTests
{
    private static Side Side(double recall = 0.90, double falsePositives = 0.10, decimal? cost = 1.00m, double p95 = 10_000, bool hard = true) => new(
        new Dictionary<string, MetricSummary>
        {
            [MetricNames.RequiredToolRecall] = new(recall, recall, recall, 20),
            [MetricNames.ProposalFalsePositiveRate] = new(falsePositives, falsePositives, falsePositives, 10),
            [MetricNames.ToolArgumentAccuracy] = new(null, null, null, 0),
        },
        hard,
        cost,
        p95);

    private static readonly Side Agent = Side();

    [Fact]
    public void Equivalent_and_25_percent_cheaper_simplifies() =>
        Assert.Equal(KillTestResult.Simplify, KillTest.Decide(Agent, Side(cost: 0.75m)));

    [Fact]
    public void Equivalent_and_25_percent_faster_at_p95_simplifies() =>
        Assert.Equal(KillTestResult.Simplify, KillTest.Decide(Agent, Side(p95: 7_500)));

    [Fact]
    public void A_better_p50_with_a_materially_worse_p95_does_not_simplify() =>
        Assert.Equal(KillTestResult.KeepAgentMixed, KillTest.Decide(Agent, Side(p95: 13_000)));

    [Fact]
    public void An_unpriced_side_makes_the_cost_arm_unavailable()
    {
        Assert.Equal(KillTestResult.KeepAgentMixed, KillTest.Decide(Agent, Side(cost: null)));
        Assert.Equal(KillTestResult.KeepAgentMixed, KillTest.Decide(Agent with { RuntimeChatCost = null }, Side(cost: 0.10m)));
    }

    [Fact]
    public void Equivalent_but_only_20_percent_cheaper_keeps_the_agent_mixed() =>
        Assert.Equal(KillTestResult.KeepAgentMixed, KillTest.Decide(Agent, Side(cost: 0.80m)));

    [Fact]
    public void More_than_005_worse_on_any_gating_metric_keeps_the_agent() =>
        Assert.Equal(KillTestResult.KeepAgent, KillTest.Decide(Agent, Side(recall: 0.84, cost: 0.10m)));

    [Fact]
    public void False_positive_rate_compares_in_the_other_direction()
    {
        Assert.Equal(KillTestResult.KeepAgent, KillTest.Decide(Agent, Side(falsePositives: 0.16, cost: 0.10m)));
        Assert.Equal(KillTestResult.Simplify, KillTest.Decide(Agent, Side(falsePositives: 0.00, cost: 0.10m)));
    }

    [Fact]
    public void A_challenger_hard_gate_failure_keeps_the_agent() =>
        Assert.Equal(KillTestResult.KeepAgent, KillTest.Decide(Agent, Side(hard: false, cost: 0.10m)));

    [Fact]
    public void Exactly_005_worse_is_still_equivalent() =>
        Assert.Equal(KillTestResult.Simplify, KillTest.Decide(Agent, Side(recall: 0.85, falsePositives: 0.15, cost: 0.75m)));
}
