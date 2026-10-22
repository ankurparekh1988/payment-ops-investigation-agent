using System.Diagnostics;
using System.Text.Json;

using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentOps.Agent;
using PaymentOps.Agent.Contracts;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Agent;

/// <summary>
/// Runs each seeded task through the same registration and service seam the app uses, against the deployed
/// model and index, and records what the model did. Writes nothing anywhere but the report.
/// </summary>
internal static class AgentRun
{
    public static async Task<AgentRunReport> RunAsync(IReadOnlyList<AgentTask> tasks, AiRelease release, InvestigationEndpoints endpoints, TokenCredential credential)
    {
        var results = new List<AgentTaskResult>();
        foreach (var task in tasks)
        {
            var stats = new RunStats();
            using var provider = Services(release, endpoints, credential, new EvaluationUser(task.Access == KnowledgeClassification.Restricted), stats);
            using var scope = provider.CreateScope();

            var timer = Stopwatch.StartNew();
            var result = await scope.ServiceProvider.GetRequiredService<IInvestigationService>().InvestigateAsync(task.Question, CancellationToken.None);
            results.Add(new AgentTaskResult(task, stats.Calls, Describe(result), result is AssessmentResult.Assessed { Outcome: not AssessmentOutcome.Refused }, timer.ElapsedMilliseconds, stats.Requests, stats.InputTokens, stats.OutputTokens));
        }

        return new AgentRunReport(results);
    }

    private static string Describe(AssessmentResult result) => result switch
    {
        AssessmentResult.Assessed a => $"{ToolJson.Word(a.Outcome)} ({(a.RootCause is { } root ? ToolJson.Word(root.Label) : "no root cause")}, {ToolJson.Word(a.Confidence)}, {a.Gaps.Count} gaps)",
        AssessmentResult.NotAssessed n => $"not assessed: {ToolJson.Word(n.Reason)}",
        _ => "unknown",
    };

    private static ServiceProvider Services(AiRelease release, InvestigationEndpoints endpoints, TokenCredential credential, IUserContext user, RunStats stats)
    {
        var services = new ServiceCollection();
        services.AddSingleton(release);
        services.AddSingleton<IPromptProvider>(new PromptProvider(release));
        services.AddSingleton(credential);
        services.AddSingleton(user);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDemoData(new ConfigurationBuilder().Build());

        // The model client the app would use, observed from outside so the run sees every request and tool call.
        services.AddInvestigation(endpoints, model => new RecordingChatClient(model, stats));
        return services.BuildServiceProvider();
    }

    /// <summary>What one task's model requests added up to; tool calls, never model text.</summary>
    private sealed class RunStats
    {
        public List<ToolCallRecord> Calls { get; } = [];

        public int Requests { get; set; }

        public long InputTokens { get; set; }

        public long OutputTokens { get; set; }
    }

    private sealed class RecordingChatClient(IChatClient inner, RunStats stats) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            stats.Requests++;
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            stats.InputTokens += response.Usage?.InputTokenCount ?? 0;
            stats.OutputTokens += response.Usage?.OutputTokenCount ?? 0;
            stats.Calls.AddRange(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
                .Select(call => new ToolCallRecord(call.Name, call.Name == ToolNames.SubmitAssessment ? "{}" : JsonSerializer.Serialize(call.Arguments))));
            return response;
        }
    }

    /// <summary>A signed-in Ops Reader, with or without Restricted knowledge access.</summary>
    private sealed record EvaluationUser(bool CanAccessRestrictedKnowledge) : IUserContext
    {
        public bool IsAuthenticated => true;

        public string UserId => "evaluation";

        public string DisplayName => "Agent evaluation";

        public IReadOnlySet<string> Roles { get; } = OpsRoles.Effective([OpsRoles.Reader]);

        public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
