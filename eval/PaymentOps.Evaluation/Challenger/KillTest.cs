using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Challenger;

internal enum KillTestResult
{
    /// <summary>The fixed workflow is as good and materially cheaper or faster: recommend replacing the agent.</summary>
    Simplify,

    /// <summary>The fixed workflow is worse on a gating metric or failed a hard gate.</summary>
    KeepAgent,

    /// <summary>Neither worse nor materially better; the mixed result is recorded.</summary>
    KeepAgentMixed,
}

/// <summary>One system's three-run results, as the decision rule compares them.</summary>
/// <param name="RuntimeChatCost">The system's own chat calls priced from the frozen price list, input and output; never the judge. Null when unpriced.</param>
/// <param name="P95LatencyMs">95th percentile of full-investigation latency over every case in every run; null when nothing was measured.</param>
internal sealed record Side(IReadOnlyDictionary<string, MetricSummary> Summary, bool HardPassedEveryRun, decimal? RuntimeChatCost, double? P95LatencyMs);

/// <summary>
/// The pre-registered agent-versus-fixed-workflow rule, fixed before either side ran. The challenger must pass
/// every hard gate, be within <see cref="Tolerance"/> on every gating median, and be at least
/// <see cref="MaterialAdvantage"/> cheaper or faster at the tail to replace the agent.
/// </summary>
internal static class KillTest
{
    public const decimal Tolerance = 0.05m;
    public const decimal MaterialAdvantage = 0.25m;

    public static KillTestResult Decide(Side agent, Side challenger)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(challenger);

        if (!challenger.HardPassedEveryRun || ThresholdRule.Gating.Any(metric => Worse(metric, agent, challenger) > Tolerance))
        {
            return KillTestResult.KeepAgent;
        }

        // An unpriced or unmeasured side has no advantage on that arm: it is never taken as zero or estimated.
        var cheaper = agent.RuntimeChatCost is > 0 && challenger.RuntimeChatCost is { } cost && cost <= agent.RuntimeChatCost * (1 - MaterialAdvantage);
        var faster = agent.P95LatencyMs is > 0 && challenger.P95LatencyMs is { } p95 && (decimal)p95 <= (decimal)agent.P95LatencyMs.Value * (1 - MaterialAdvantage);
        return cheaper || faster ? KillTestResult.Simplify : KillTestResult.KeepAgentMixed;
    }

    // How far the challenger's median is behind the agent's, in decimal so a gap of exactly 0.05 is exactly 0.05.
    private static decimal Worse(string metric, Side agent, Side challenger)
    {
        if (agent.Summary.GetValueOrDefault(metric)?.Median is not { } a || challenger.Summary.GetValueOrDefault(metric)?.Median is not { } c)
        {
            return 0;
        }

        return ThresholdRule.LowerIsBetter.Contains(metric) ? (decimal)c - (decimal)a : (decimal)a - (decimal)c;
    }
}
