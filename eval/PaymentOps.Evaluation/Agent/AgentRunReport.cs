using System.Globalization;
using System.Text;
using System.Text.Json;

using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Agent;

/// <param name="Arguments">The arguments as the model sent them: ids and dates only, by the tools' contracts.</param>
internal sealed record ToolCallRecord(string Tool, string Arguments);

/// <param name="Outcome">The server's verdict in a few words; model text is never recorded.</param>
/// <param name="Assessed">The server accepted an assessment that wasn't a refusal.</param>
internal sealed record AgentTaskResult(AgentTask Task, IReadOnlyList<ToolCallRecord> Calls, string Outcome, bool Assessed, long LatencyMs, int ModelRequests, long InputTokens, long OutputTokens)
{
    private IReadOnlyList<string> ReadToolsCalled => [.. Calls.Select(c => c.Tool).Where(ToolNames.ReadTools.Contains).Distinct()];

    public int IntendedToolsCalled => Task.IntendedTools.Count(ReadToolsCalled.Contains);

    public IReadOnlyList<string> IntendedToolsMissed => [.. Task.IntendedTools.Where(tool => !ReadToolsCalled.Contains(tool))];

    public IReadOnlyList<string> OtherToolsCalled => [.. ReadToolsCalled.Where(tool => !Task.IntendedTools.Contains(tool))];

    public bool IsGenuineAssessment => Assessed && ReadToolsCalled.Count >= 2;
}

/// <summary>
/// The recorded live run: each trajectory, its agreement with the intended tools, the validated outcome,
/// latency and tokens. Deliberately no thresholds; the one guard is that the agent works at all.
/// </summary>
internal sealed class AgentRunReport(IReadOnlyList<AgentTaskResult> results)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    public IReadOnlyList<AgentTaskResult> Results { get; } = results;

    /// <summary>At least one task where the model chose several tools on its own and its assessment passed validation.</summary>
    public bool GuardPassed => Results.Any(r => r.IsGenuineAssessment);

    public string ToJson() => JsonSerializer.Serialize(
        new
        {
            GuardPassed,
            Results = Results.Select(r => new
            {
                r.Task.Id,
                r.Task.Scenario,
                r.Task.Access,
                r.Task.Question,
                r.Task.IntendedTools,
                Calls = r.Calls,
                r.IntendedToolsCalled,
                r.IntendedToolsMissed,
                r.OtherToolsCalled,
                r.Outcome,
                r.LatencyMs,
                r.ModelRequests,
                r.InputTokens,
                r.OutputTokens,
            }),
        },
        Json);

    public string ToMarkdown()
    {
        var md = new StringBuilder();
        md.AppendLine("## Investigation agent live run").AppendLine();
        md.AppendLine(GuardPassed
            ? "At least one task produced a multi-tool trajectory ending in a server-validated assessment."
            : "**No task produced a multi-tool trajectory ending in a server-validated assessment. The agent integration isn't proven.**");
        md.AppendLine();
        md.AppendLine("| Task | Access | Tools called (in order) | Intended called | Outcome | Latency | Model requests | Tokens in / out |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in Results)
        {
            var calls = r.Calls.Count == 0 ? "none" : string.Join(" → ", r.Calls.Select(c => c.Tool));
            md.AppendLine(CultureInfo.InvariantCulture, $"| {r.Task.Id} | {r.Task.Access} | {calls} | {r.IntendedToolsCalled}/{r.Task.IntendedTools.Count} | {r.Outcome} | {r.LatencyMs / 1000.0:0.0} s | {r.ModelRequests} | {r.InputTokens} / {r.OutputTokens} |");
        }

        return md.ToString();
    }
}
