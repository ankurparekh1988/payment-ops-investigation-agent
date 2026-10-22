using System.Diagnostics;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Model;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Platform.Ai;
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
    IPromptProvider prompts,
    IInvestigationClock clock,
    IUserContext user,
    IOptions<InvestigationLimits> options,
    TimeProvider time,
    ILogger<InvestigationAgentRunner> logger)
{
    public const string PromptId = "investigation-agent";

    private static readonly Dictionary<string, string> NoValues = [];
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
        var guard = new ToolGuard(turn, _limits, time, user, progress);
        List<AITool> readTools = [.. tools.Where(tool => ToolAuthorization.Allows(user, tool.Name)).Select(guard.Wrap)];

        // No default tools: tools passed per run are added to an agent's defaults, and the correction run must offer only submit_assessment.
        var agent = new ChatClientAgent(InvestigationPipeline.FunctionInvoking(model, _limits), new ChatClientAgentOptions
        {
            Name = "ops-investigation",
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions(),
                MaxOutputTokens = _limits.MaxOutputTokens,
                AllowMultipleToolCalls = false,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });

        using var turnTimeout = new CancellationTokenSource(_limits.TurnTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turnTimeout.Token);
        try
        {
            // A fresh session every turn: earlier turns arrive only as the plain-text history, never as tool messages.
            var session = await agent.CreateSessionAsync(linked.Token);
            var first = await agent.RunAsync([.. history, new ChatMessage(ChatRole.User, question)], session, Run([.. readTools, submit], null), linked.Token);

            // A call no tool answered (an unknown tool) ends the investigation: fail closed, and that history can't be sent again.
            if (turn.Accepted is null && HasUnansweredCall(first.Messages))
            {
                return Unverified;
            }

            // One more request either way: to correct a rejected submission, or to submit at all.
            if (turn.Accepted is null)
            {
                await agent.RunAsync([], session, Run([submit], ChatToolMode.RequireSpecific(ToolNames.SubmitAssessment)), linked.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NotAssessed(NotAssessedReason.Cancelled);
        }
        catch (OperationCanceledException) when (turnTimeout.IsCancellationRequested)
        {
            if (!turn.Stopped)
            {
                progress?.Report(new InvestigationProgress.LimitReached());
            }

            turn.Stop();
        }
        catch (OperationCanceledException ex)
        {
            // Cancelled by neither the caller nor our turn timeout: the model or Search client timed out on its own.
            InvestigationLog.ModelFailed(logger, investigationId, ex, timer.ElapsedMilliseconds);
            return new NotAssessed(NotAssessedReason.ModelUnavailable);
        }
        catch (Exception ex) when (ModelFailure.IsServiceFailure(ex))
        {
            InvestigationLog.ModelFailed(logger, investigationId, ex, timer.ElapsedMilliseconds);
            return new NotAssessed(NotAssessedReason.ModelUnavailable);
        }
        catch (Exception ex)
        {
            // A tool or the loop failed unexpectedly; the framework ended the loop without retrying, and so do we.
            InvestigationLog.Aborted(logger, investigationId, ex, timer.ElapsedMilliseconds);
            return new NotAssessed(NotAssessedReason.AssessmentUnverified);
        }

        // Cancelled after the submission was accepted but before we got here: still nothing is kept.
        if (cancellationToken.IsCancellationRequested)
        {
            return new NotAssessed(NotAssessedReason.Cancelled);
        }

        return turn.Accepted is { } submission ? AssessmentPolicy.Apply(submission, turn)
            : new NotAssessed(turn.Stopped ? NotAssessedReason.LimitReached : NotAssessedReason.AssessmentUnverified);
    }

    /// <summary>The instructions and every tool definition, as sent with each request: the fixed part of the input budget.</summary>
    public int FixedPromptChars()
    {
        var guard = new ToolGuard(new TurnState(), _limits, time, user, null);
        AIFunction[] definitions = [.. tools.Select(guard.Wrap), new SubmitAssessmentFunction(new TurnState(), clock.Today, null)];
        return Instructions().Length + definitions.Sum(f => f.Name.Length + f.Description.Length + f.JsonSchema.GetRawText().Length);
    }

    private static readonly NotAssessed Unverified = new(NotAssessedReason.AssessmentUnverified);

    private static bool HasUnansweredCall(IList<ChatMessage> messages)
    {
        var answered = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId).ToHashSet(StringComparer.Ordinal);
        return messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any(call => !answered.Contains(call.CallId));
    }

    private static ChatClientAgentRunOptions Run(IList<AITool> runTools, ChatToolMode? mode) =>
        new(new ChatOptions { Tools = runTools, ToolMode = mode });

    // The current time comes from the investigation clock, so "yesterday" is the data's yesterday, not the model's.
    private string Instructions() =>
        $"{prompts.GetPrompt(PromptId).Template.Render(NoValues)}\n\nNow: {clock.Now:yyyy-MM-dd HH:mm} UTC. Today is {clock.Today:yyyy-MM-dd}.";
}
