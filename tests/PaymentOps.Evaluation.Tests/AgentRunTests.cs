using System.Text.Json.Nodes;

using PaymentOps.Agent.Contracts;
using PaymentOps.Evaluation.Agent;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Tests;

public sealed class AgentRunTests
{
    private static readonly AgentTask S1 = new("s1", "S1", "general", "reader", "Why?", [ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.KnowledgeSearch], ExpectProposal: true);

    private static AgentTaskResult Result(AgentTask task, string outcome, params string[] calls) =>
        new(task, [.. calls.Select(c => new ToolCallRecord(c, "{}"))], outcome, outcome.StartsWith("assessed", StringComparison.Ordinal), LatencyMs: 4200, ModelRequests: calls.Length + 1, InputTokens: 9000, OutputTokens: 600);

    [Fact]
    public void The_seeded_tasks_load_and_name_only_real_tools()
    {
        var tasks = AgentTaskDataset.Load(Path.Combine(RepositoryRoot.FullPath, "eval", "datasets", "agent-tasks.json"));

        Assert.InRange(tasks.Count, 8, 15);
        Assert.Equal(tasks.Count, tasks.Select(t => t.Id).Distinct().Count());
        Assert.All(tasks.SelectMany(t => t.IntendedTools), tool => Assert.Contains(tool, ToolNames.ReadTools));
        Assert.Contains(tasks, t => t.Access == "restricted");
    }

    [Fact]
    public void A_task_naming_an_unknown_tool_is_rejected()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "description": "x", "tasks": [ { "id": "a", "scenario": "S1", "access": "general", "role": "reader", "question": "q", "intendedTools": ["get_everything"], "expectProposal": false } ] }""");

        Assert.Throws<InvalidDataException>(() => AgentTaskDataset.Load(path));
    }

    [Fact]
    public void A_task_with_an_unknown_role_is_rejected()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{ "description": "x", "tasks": [ { "id": "a", "scenario": "S1", "access": "general", "role": "admin", "question": "q", "intendedTools": [], "expectProposal": false } ] }""");

        Assert.Throws<InvalidDataException>(() => AgentTaskDataset.Load(path));
    }

    // A runbook says to raise an incident for a missing or duplicated processor file; nothing else warrants one.
    [Fact]
    public void Proposals_are_expected_only_where_a_runbook_says_to_raise_an_incident()
    {
        var tasks = AgentTaskDataset.Load(Path.Combine(RepositoryRoot.FullPath, "eval", "datasets", "agent-tasks.json"));

        Assert.All(tasks, t => Assert.Equal(t.Scenario is "S1" or "S4", t.ExpectProposal));
        Assert.Contains(tasks, t => t.Id == "s1-late-payout-engineer" && t.Role == "engineer");
    }

    [Fact]
    public void Time_to_first_progress_starts_at_the_first_agent_driven_event()
    {
        var clock = new Queue<long>([100, 900, 1500]);
        var sink = new FirstProgress(() => clock.Dequeue());

        sink.Report(new InvestigationProgress.LimitReached());
        sink.Report(new InvestigationProgress.ToolStarted(ToolNames.MerchantProfile, "Merchant profile"));
        sink.Report(new InvestigationProgress.Submitting());

        Assert.Equal(900, sink.FirstMs);
    }

    [Fact]
    public void A_direct_submission_counts_as_the_first_progress()
    {
        var sink = new FirstProgress(() => 300);

        sink.Report(new InvestigationProgress.Submitting());

        Assert.Equal(300, sink.FirstMs);
    }

    [Fact]
    public void The_report_shows_time_to_first_progress_and_proposals()
    {
        var engineer = S1 with { Id = "s1-engineer", Role = "engineer" };
        var report = new AgentRunReport(
        [
            Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches) with { TtfpMs = 1200, Proposal = "draft" },
            Result(engineer, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches) with
            {
                TtfpMs = 2600,
                Proposal = "approved: INC-0001, reference FAB-7731",
                TokenEstimates = [new TokenEstimate(5000, 4600)],
            },
        ]);

        var markdown = report.ToMarkdown();
        Assert.Contains("Time to first progress: p50 1.2 s, p95 2.6 s", markdown, StringComparison.Ordinal);
        Assert.Contains("approved: INC-0001, reference FAB-7731", markdown, StringComparison.Ordinal);
        Assert.Contains("| Proposal (expected) |", markdown, StringComparison.Ordinal);
        Assert.Contains("5000 / 4600", markdown, StringComparison.Ordinal);
        var json = JsonNode.Parse(report.ToJson())!;
        Assert.Equal(2600, (int)json["results"]![1]!["ttfp_ms"]!);
        Assert.True((bool)json["results"]![1]!["expect_proposal"]!);
    }

    [Fact]
    public void Agreement_counts_the_intended_tools_the_model_chose_and_lists_the_rest()
    {
        var result = Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.ErrorEvents, ToolNames.SubmitAssessment);

        Assert.Equal(2, result.IntendedToolsCalled);
        Assert.Equal([ToolNames.KnowledgeSearch], result.IntendedToolsMissed);
        Assert.Equal([ToolNames.ErrorEvents], result.OtherToolsCalled);
    }

    // The one guard this run has: at least one task where the model chose several tools and its assessment passed validation.
    [Fact]
    public void The_guard_needs_a_multi_tool_trajectory_ending_in_a_valid_assessment()
    {
        var oneTool = Result(S1, "assessed (hypothesis, medium)", ToolNames.MerchantProfile, ToolNames.SubmitAssessment);
        var unverified = Result(S1, "not assessed: AssessmentUnverified", ToolNames.MerchantProfile, ToolNames.SettlementBatches);
        var genuine = Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.SubmitAssessment);

        Assert.False(new AgentRunReport([oneTool, unverified]).GuardPassed);
        Assert.True(new AgentRunReport([oneTool, genuine]).GuardPassed);
    }

    [Fact]
    public void The_report_records_trajectories_latency_and_tokens_without_thresholds()
    {
        var report = new AgentRunReport([Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.SubmitAssessment)]);

        var json = JsonNode.Parse(report.ToJson())!;
        Assert.Equal(ToolNames.SettlementBatches, (string?)json["results"]![0]!["calls"]![1]!["tool"]);
        Assert.Equal(4200, (int)json["results"]![0]!["latency_ms"]!);
        Assert.True((bool)json["guard_passed"]!);

        var markdown = report.ToMarkdown();
        Assert.Contains("| s1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("4.2 s", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("threshold", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
