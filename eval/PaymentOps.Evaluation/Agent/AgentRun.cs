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
using PaymentOps.Agent.Conversations;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Agent;

/// <summary>
/// Runs each seeded task through the same registration and service seam the app uses, against the deployed
/// model and index, and records what the model did. An engineer task approves its own pending proposal into the
/// run's in-memory ticket store, which goes with the run; nothing is written anywhere but the report.
/// </summary>
internal static class AgentRun
{
    public static async Task<AgentRunReport> RunAsync(IReadOnlyList<AgentTask> tasks, AiRelease release, InvestigationEndpoints endpoints, TokenCredential credential)
    {
        var results = new List<AgentTaskResult>();
        foreach (var task in tasks)
        {
            var stats = new RunStats();
            var user = new EvaluationUser(task.Access == KnowledgeClassification.Restricted, task.Role == AgentTask.Engineer ? OpsRoles.Engineer : OpsRoles.Reader);
            using var provider = Services(release, endpoints, credential, user, stats);
            using var scope = provider.CreateScope();

            var timer = Stopwatch.StartNew();
            var first = new FirstProgress(() => timer.ElapsedMilliseconds);
            var result = await scope.ServiceProvider.GetRequiredService<IInvestigationService>().InvestigateAsync(task.Question, first, CancellationToken.None);
            var latency = timer.ElapsedMilliseconds;
            var proposal = await ProposalAsync(result, scope.ServiceProvider.GetRequiredService<IApprovalService>());
            results.Add(new AgentTaskResult(task, stats.Calls, Describe(result), result is AssessmentResult.Assessed { Outcome: not AssessmentOutcome.Refused }, latency, stats.Requests, stats.InputTokens, stats.OutputTokens)
            {
                TtfpMs = first.FirstMs,
                Proposal = proposal,
                TokenEstimates = stats.Estimates,
            });
        }

        return new AgentRunReport(results);
    }

    // A pending proposal is approved the way its owner would, so the run shows the ticket and its reference.
    private static async Task<string> ProposalAsync(AssessmentResult result, IApprovalService approvals)
    {
        if (result is not AssessmentResult.Assessed assessed)
        {
            return "none";
        }

        if (assessed.Proposal is not { } proposal)
        {
            return assessed.ProposalWithheld ? "withheld" : "none";
        }

        if (proposal is not { Status: ProposalStatus.Pending, ProposalId: { } id })
        {
            return ToolJson.Word(proposal.Status);
        }

        return await approvals.ApproveAsync(id, CancellationToken.None) switch
        {
            ApprovalResult.Created created => $"approved: {created.TicketId}, reference {proposal.ProcessorReference ?? "none"}",
            var other => $"approval failed: {other.GetType().Name}",
        };
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

        public List<TokenEstimate> Estimates { get; } = [];
    }

    private sealed class RecordingChatClient(IChatClient inner, RunStats stats) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            stats.Requests++;
            var sent = messages.ToList();
            var response = await base.GetResponseAsync(sent, options, cancellationToken);
            stats.InputTokens += response.Usage?.InputTokenCount ?? 0;
            stats.Estimates.Add(new TokenEstimate(EstimatedTokens(sent, options), response.Usage?.InputTokenCount ?? 0));
            stats.OutputTokens += response.Usage?.OutputTokenCount ?? 0;
            stats.Calls.AddRange(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
                .Select(call => new ToolCallRecord(call.Name, call.Name == ToolNames.SubmitAssessment ? "{}" : JsonSerializer.Serialize(call.Arguments))));
            return response;
        }
    }

    // The app's own estimate, over everything the request carried, so it can be compared with what was counted.
    private static long EstimatedTokens(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var chars = (options?.Instructions?.Length ?? 0)
            + (options?.Tools?.OfType<AIFunction>().Sum(PromptSize.DefinitionChars) ?? 0)
            + messages.SelectMany(m => m.Contents).Sum(content => content switch
            {
                TextContent text => text.Text.Length,
                FunctionCallContent call => call.Name.Length + JsonSerializer.Serialize(call.Arguments).Length,
                FunctionResultContent result => result.Result?.ToString()?.Length ?? 0,
                _ => 0,
            });
        return PromptSize.Tokens(chars);
    }

    /// <summary>A signed-in Ops Reader or Engineer, with or without Restricted knowledge access.</summary>
    private sealed record EvaluationUser(bool CanAccessRestrictedKnowledge, string Role) : IUserContext
    {
        public bool IsAuthenticated => true;

        public string UserId => "evaluation";

        public string DisplayName => "Agent evaluation";

        public IReadOnlySet<string> Roles { get; } = OpsRoles.Effective([Role]);

        public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
