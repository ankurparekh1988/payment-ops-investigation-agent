using System.Text.Json.Nodes;

using PaymentOps.Evaluation.Agent;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Tests;

public sealed class AgentRunTests
{
    private static readonly AgentTask S1 = new("s1", "S1", "general", "Why?", [ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.KnowledgeSearch]);

    private static AgentTaskResult Result(AgentTask task, string outcome, params string[] calls) =>
        new(task, [.. calls.Select(c => new ToolCallRecord(c, "{}"))], outcome, LatencyMs: 4200, ModelRequests: calls.Length + 1, InputTokens: 9000, OutputTokens: 600);

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
        File.WriteAllText(path, """{ "description": "x", "tasks": [ { "id": "a", "scenario": "S1", "access": "general", "question": "q", "intendedTools": ["get_everything"] } ] }""");

        Assert.Throws<InvalidDataException>(() => AgentTaskDataset.Load(path));
    }

    [Fact]
    public void Agreement_counts_the_intended_tools_the_model_chose_and_lists_the_rest()
    {
        var result = Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.ErrorEvents, "submit_assessment");

        Assert.Equal(2, result.IntendedToolsCalled);
        Assert.Equal([ToolNames.KnowledgeSearch], result.IntendedToolsMissed);
        Assert.Equal([ToolNames.ErrorEvents], result.OtherToolsCalled);
    }

    // The one guard this run has: at least one task where the model chose several tools and its assessment passed validation.
    [Fact]
    public void The_guard_needs_a_multi_tool_trajectory_ending_in_a_valid_assessment()
    {
        var oneTool = Result(S1, "assessed (hypothesis, medium)", ToolNames.MerchantProfile, "submit_assessment");
        var unverified = Result(S1, "not assessed: AssessmentUnverified", ToolNames.MerchantProfile, ToolNames.SettlementBatches);
        var genuine = Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches, "submit_assessment");

        Assert.False(new AgentRunReport([oneTool, unverified]).GuardPassed);
        Assert.True(new AgentRunReport([oneTool, genuine]).GuardPassed);
    }

    [Fact]
    public void The_report_records_trajectories_latency_and_tokens_without_thresholds()
    {
        var report = new AgentRunReport([Result(S1, "assessed (assessment, high)", ToolNames.MerchantProfile, ToolNames.SettlementBatches, "submit_assessment")]);

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
