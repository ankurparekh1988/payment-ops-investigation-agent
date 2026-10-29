using System.Diagnostics;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Tools;

using NotAssessed = PaymentOps.Agent.Contracts.AssessmentResult.NotAssessed;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// One investigation: the model chooses read tools until it submits an assessment, which is shown only after
/// it checks out against this turn's evidence. Earlier turns come in as plain text and register no evidence.
/// </summary>
internal sealed class InvestigationAgentRunner(
    IChatClient model,
    IEnumerable<IInvestigationTool> tools,
    FixedPrompt fixedPrompt,
    IInvestigationClock clock,
    IUserContext user,
    IOptions<InvestigationLimits> options,
    TimeProvider time,
    IUntrustedTextScreen screen,
    ILogger<InvestigationAgentRunner> logger)
{
    public const string PromptId = "investigation-agent";

    private readonly InvestigationLimits _limits = options.Value;

    /// <param name="history">Earlier turns as plain text: context for the question, never evidence for it.</param>
    public async Task<AssessmentResult> InvestigateAsync(
        string question, IReadOnlyList<ChatMessage> history, IProgress<InvestigationProgress>? progress, Guid investigationId, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var turn = new TurnState();
        var result = await RunAsync(question, history, progress, investigationId, turn, timer, cancellationToken) with { InvestigationId = investigationId };
        InvestigationLog.Completed(logger, result, turn.Gaps.Count, timer.ElapsedMilliseconds);
        return result;
    }

    private async Task<AssessmentResult> RunAsync(
        string question, IReadOnlyList<ChatMessage> history, IProgress<InvestigationProgress>? progress, Guid investigationId, TurnState turn, Stopwatch timer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > _limits.MaxQuestionLength)
        {
            return new NotAssessed(NotAssessedReason.InvalidQuestion);
        }

        if (!user.IsAuthenticated || !user.IsInRole(OpsRoles.Reader))
        {
            return new NotAssessed(NotAssessedReason.NotAuthorized);
        }

        var submit = new SubmitAssessmentFunction(turn, clock.Today, progress);
        var guard = new ToolGuard(turn, _limits, time, user, progress, screen, logger, investigationId);
        List<AITool> readTools = [.. tools.Where(tool => ToolAuthorization.Allows(user, tool.Name)).Select(guard.Wrap)];

        // No default tools: tools passed per run are added to an agent's defaults, and the correction run must offer only submit_assessment.
        var agent = new ChatClientAgent(InvestigationPipeline.FunctionInvoking(model, _limits), new ChatClientAgentOptions
        {
            Name = "ops-investigation",
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = fixedPrompt.Instructions(),
                MaxOutputTokens = _limits.MaxOutputTokens,
                AllowMultipleToolCalls = false,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });

        return await TurnOutcome.RunAsync(
            turn,
            _limits,
            time,
            progress,
            async token =>
            {
                // A fresh session every turn: earlier turns arrive only as the plain-text history, never as tool messages.
                var session = await agent.CreateSessionAsync(token);
                var first = await agent.RunAsync([.. history, new ChatMessage(ChatRole.User, question)], session, Run([.. readTools, submit], null), token);

                // A call no tool answered (an unknown tool) ends the investigation: fail closed, and that history can't be sent again.
                if (turn.Accepted is null && HasUnansweredCall(first.Messages))
                {
                    return Unverified;
                }

                // One more request either way: to correct a rejected submission, or to submit at all.
                if (turn.Accepted is null)
                {
                    await agent.RunAsync([], session, Run([submit], ChatToolMode.RequireSpecific(ToolNames.SubmitAssessment)), token);
                }

                return null;
            },
            (failure, ex) =>
            {
                if (failure == TurnFailure.ServiceFailed)
                {
                    InvestigationLog.ModelFailed(logger, investigationId, ex, timer.ElapsedMilliseconds);
                }
                else
                {
                    InvestigationLog.Aborted(logger, investigationId, ex, timer.ElapsedMilliseconds);
                }
            },
            cancellationToken);
    }

    private static readonly NotAssessed Unverified = new(NotAssessedReason.AssessmentUnverified);

    private static bool HasUnansweredCall(IList<ChatMessage> messages)
    {
        var answered = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId).ToHashSet(StringComparer.Ordinal);
        return messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any(call => !answered.Contains(call.CallId));
    }

    private static ChatClientAgentRunOptions Run(IList<AITool> runTools, ChatToolMode? mode) =>
        new(new ChatOptions { Tools = runTools, ToolMode = mode });
}
