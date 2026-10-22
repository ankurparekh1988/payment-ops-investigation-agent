using System.Globalization;
using System.Text;
using System.Text.Json;

using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Agent;

/// <param name="Arguments">The arguments as the model sent them: ids and dates only, by the tools' contracts.</param>
internal sealed record ToolCallRecord(string Tool, string Arguments);

/// <summary>One model request's input size: the application's chars ÷ 3 estimate against what the service counted.</summary>
internal sealed record TokenEstimate(long Estimated, long Actual);

/// <param name="Outcome">The server's verdict in a few words; model text is never recorded.</param>
/// <param name="Assessed">The server accepted an assessment that wasn't a refusal.</param>
internal sealed record AgentTaskResult(AgentTask Task, IReadOnlyList<ToolCallRecord> Calls, string Outcome, bool Assessed, long LatencyMs, int ModelRequests, long InputTokens, long OutputTokens)
{
    private IReadOnlyList<string> ReadToolsCalled => [.. Calls.Select(c => c.Tool).Where(ToolNames.ReadTools.Contains).Distinct()];

    public int IntendedToolsCalled => Task.IntendedTools.Count(ReadToolsCalled.Contains);

    public IReadOnlyList<string> IntendedToolsMissed => [.. Task.IntendedTools.Where(tool => !ReadToolsCalled.Contains(tool))];

    public IReadOnlyList<string> OtherToolsCalled => [.. ReadToolsCalled.Where(tool => !Task.IntendedTools.Contains(tool))];

    public bool IsGenuineAssessment => Assessed && ReadToolsCalled.Count >= 2;

    /// <summary>Submit to the first agent-driven event; null when the agent never got that far.</summary>
    public long? TtfpMs { get; init; }

    /// <summary>What became of a proposal, in the server's words: none, draft, pending, approved with its ticket, and so on.</summary>
    public string Proposal { get; init; } = "none";

    public IReadOnlyList<TokenEstimate> TokenEstimates { get; init; } = [];
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
                r.Task.Role,
                r.Task.Question,
                r.Task.IntendedTools,
                Calls = r.Calls,
                r.IntendedToolsCalled,
                r.IntendedToolsMissed,
                r.OtherToolsCalled,
                r.Outcome,
                r.Task.ExpectProposal,
                r.Proposal,
                r.TtfpMs,
                r.TokenEstimates,
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
        var ttfp = Results.Where(r => r.TtfpMs is not null).Select(r => r.TtfpMs!.Value).Order().ToList();
        if (ttfp.Count > 0)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"Time to first progress: p50 {Rank(ttfp, 0.50) / 1000.0:0.0} s, p95 {Rank(ttfp, 0.95) / 1000.0:0.0} s (nearest rank over {ttfp.Count} questions).");
            md.AppendLine();
        }

        md.AppendLine("| Task | Access | Role | Tools called (in order) | Intended called | Outcome | Proposal (expected) | First progress | Latency | Model requests | Tokens in / out |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in Results)
        {
            var calls = r.Calls.Count == 0 ? "none" : string.Join(" → ", r.Calls.Select(c => c.Tool));
            var first = r.TtfpMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms / 1000.0:0.0} s") : "none";
            md.AppendLine(CultureInfo.InvariantCulture, $"| {r.Task.Id} | {r.Task.Access} | {r.Task.Role} | {calls} | {r.IntendedToolsCalled}/{r.Task.IntendedTools.Count} | {r.Outcome} | {r.Proposal} ({(r.Task.ExpectProposal ? "yes" : "no")}) | {first} | {r.LatencyMs / 1000.0:0.0} s | {r.ModelRequests} | {r.InputTokens} / {r.OutputTokens} |");
        }

        var estimates = Results.SelectMany(r => r.TokenEstimates).ToList();
        if (estimates.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("| Request input tokens: estimated / counted |");
            md.AppendLine("|---|");
            foreach (var e in estimates)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {e.Estimated} / {e.Actual} |");
            }
        }

        return md.ToString();
    }

    // Nearest rank: one run per question, so these describe this run, not a rate.
    private static long Rank(List<long> sorted, double percentile) => sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];
}
