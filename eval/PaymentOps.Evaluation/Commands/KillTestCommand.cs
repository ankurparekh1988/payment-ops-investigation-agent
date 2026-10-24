using System.Globalization;
using System.Text;
using System.Text.Json;

using PaymentOps.Evaluation.Challenger;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Commands;

/// <summary>kill-test: applies the pre-registered decision rule to an agent report and a fixed-workflow report of the same cases.</summary>
internal static class KillTestCommand
{
    public const string Usage = "kill-test --agent <report.json> --challenger <report.json> --out <file> --summary <file>";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    public static Task<int> RunAsync(string[] args) => ExitCodes.GuardAsync("Kill test", Usage, async () =>
    {
        var options = Options.Parse(args, ["agent", "challenger", "out", "summary"], []);
        var agent = EvaluationReport.Load(options.One("agent"));
        var challenger = EvaluationReport.Load(options.One("challenger"));
        if (agent.System != EvaluationReport.AgentSystem || challenger.System != EvaluationReport.FixedWorkflowSystem)
        {
            throw new InvalidDataException("--agent must be an agent report and --challenger a fixed-workflow report.");
        }

        if (agent.Runs.Count != challenger.Runs.Count || !agent.Stability.Select(s => s.CaseId).Order().SequenceEqual(challenger.Stability.Select(s => s.CaseId).Order()))
        {
            throw new InvalidDataException("Both reports must cover the same cases over the same number of runs.");
        }

        var (a, c) = (Side(agent), Side(challenger));
        var result = KillTest.Decide(a, c);
        var metrics = ThresholdRule.Gating.Where(m => agent.Summary.ContainsKey(m)).Order(StringComparer.Ordinal)
            .Select(m => new { Metric = m, Agent = agent.Summary[m].Median, Challenger = challenger.Summary.GetValueOrDefault(m)?.Median, Direction = ThresholdRule.Direction(m) })
            .ToList();

        await File.WriteAllTextAsync(options.One("out"), JsonSerializer.Serialize(
            new
            {
                Result = result.ToString(),
                KillTest.Tolerance,
                KillTest.MaterialAdvantage,
                Agent = new { agent.Commit, a.HardPassedEveryRun, a.RuntimeChatCost, a.P95LatencyMs },
                Challenger = new { challenger.Commit, c.HardPassedEveryRun, c.RuntimeChatCost, c.P95LatencyMs },
                Metrics = metrics,
            },
            Json));

        var md = new StringBuilder();
        md.AppendLine("## Kill test: agent against fixed workflow").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Result: **{result}** (tolerance {KillTest.Tolerance}, material advantage {KillTest.MaterialAdvantage:P0}).").AppendLine();
        md.AppendLine("| Metric | Agent median | Fixed workflow median | Better |").AppendLine("|---|---|---|---|");
        foreach (var m in metrics)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {m.Metric} | {m.Agent:F3} | {m.Challenger:F3} | {m.Direction} |");
        }

        md.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Hard gates every run: agent {a.HardPassedEveryRun}, fixed workflow {c.HardPassedEveryRun}.");
        md.AppendLine(CultureInfo.InvariantCulture, $"Runtime chat cost: agent {Money(a.RuntimeChatCost)}, fixed workflow {Money(c.RuntimeChatCost)}. Latency p95: agent {a.P95LatencyMs / 1000:0.0} s, fixed workflow {c.P95LatencyMs / 1000:0.0} s.");
        await File.AppendAllTextAsync(options.One("summary"), md.ToString());
        Console.WriteLine($"Kill test: {result}.");
        return ExitCodes.Passed;
    });

    private static Side Side(EvaluationReport report) =>
        new(report.Summary, report.Runs.All(r => r.HardGates.Passed), report.Cost.Chat, report.Latency.P95 ?? 0);

    private static string Money(decimal? amount) => amount is { } a ? a.ToString("0.0000", CultureInfo.InvariantCulture) : "not priced";
}
