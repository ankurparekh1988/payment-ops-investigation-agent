using System.Diagnostics;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Tools;

using NotAssessed = PaymentOps.Agent.Contracts.AssessmentResult.NotAssessed;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// One investigation: the model chooses read tools until it submits an assessment, which is shown only after
/// it checks out against this attempt's evidence. Earlier turns come in as plain text and register no evidence.
/// With routing on, the small model makes the first attempt, and only a capability failure starts one attempt on chat,
/// within the same budget (ADR 0023).
/// </summary>
internal sealed class InvestigationAgentRunner(
    ModelRouting models,
    IEnumerable<IInvestigationTool> tools,
    FixedPrompt fixedPrompt,
    IInvestigationClock clock,
    IUserContext user,
    IOptions<InvestigationLimits> options,
    TimeProvider time,
    IUntrustedTextScreen screen,
    IQuestionScreen questionScreen,
    ILogger<InvestigationAgentRunner> logger,
    TraceContext? trace = null)
{
    public const string PromptId = "investigation-agent";

    private readonly InvestigationLimits _limits = options.Value;

    private ILogger Logger => logger;

    private ModelRouting Models => models;

    /// <param name="history">Earlier turns as plain text: context for the question, never evidence for it.</param>
    public async Task<AssessmentResult> InvestigateAsync(
        string question, IReadOnlyList<ChatMessage> history, IProgress<InvestigationProgress>? progress, Guid investigationId, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var (result, gaps) = await RunAsync(question, history, progress, investigationId, timer, cancellationToken);
        result = result with { InvestigationId = investigationId };
        InvestigationLog.Completed(logger, result, gaps, timer.ElapsedMilliseconds);
        return result;
    }

    private async Task<(AssessmentResult Result, int Gaps)> RunAsync(
        string question, IReadOnlyList<ChatMessage> history, IProgress<InvestigationProgress>? progress, Guid investigationId, Stopwatch timer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > _limits.MaxQuestionLength)
        {
            return (new NotAssessed(NotAssessedReason.InvalidQuestion), 0);
        }

        if (!user.IsAuthenticated || !user.IsInRole(OpsRoles.Reader))
        {
            return (new NotAssessed(NotAssessedReason.NotAuthorized), 0);
        }

        // Before the investigation agent sees it: a question that is harmful, an attack, or couldn't be checked never reaches it.
        QuestionVerdict verdict;
        using (var screening = AgentTelemetry.Source.StartActivity("screen.question"))
        {
            try
            {
                verdict = await questionScreen.ScreenAsync(question, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (new NotAssessed(NotAssessedReason.Cancelled), 0);
            }

            var name = ToolJson.Word(verdict);
            screening?.SetTag("paymentops.screen.verdict", name);
            TurnUsage.RecordScreen("question", name);
        }

        if (verdict != QuestionVerdict.Clean)
        {
            InvestigationLog.QuestionBlocked(logger, investigationId, verdict, timer.ElapsedMilliseconds);
            return (new NotAssessed(verdict switch
            {
                QuestionVerdict.SelfHarm => NotAssessedReason.BlockedSelfHarm,
                QuestionVerdict.Unavailable => NotAssessedReason.Unscreened,
                _ => NotAssessedReason.Blocked,
            }), 0);
        }

        // One budget for the whole investigation: every attempt shares its deadline, tool calls, model calls and tokens.
        using var budget = new InvestigationBudget(_limits, time);
        var reads = new ReadCache();
        var named = new MerchantSources([question, .. history.Where(m => m.Role == ChatRole.User).Select(m => m.Text)]);
        List<ChatMessage> messages = [.. history, new ChatMessage(ChatRole.User, question)];

        TurnUsage.RecordRouting(ToolJson.Word(models.Mode));
        var first = Prepare(models.FirstTier, progress, investigationId, named, budget, reads);
        var result = await first.RunAsync(messages, budget, progress, timer, cancellationToken);
        TurnUsage.RecordAttempt(new AttemptRecord(first.Tier, first.Turn.Failure));
        if (first.Tier != ModelTier.Small || !Escalation.IsEligible(first.Turn.Failure))
        {
            TurnUsage.RecordEscalation("none");
            return (result, first.Turn.Gaps.Count);
        }

        // A fresh attempt: chat never sees the small model's submission, tool calls or evidence ids.
        var second = Prepare(ModelTier.Chat, progress, investigationId, named, budget, reads);
        var refused = budget.RefuseEscalation(RequestSizeEstimate.FirstCall(messages, second.FirstRequestOptions));
        var decision = refused is null ? "escalated" : $"refused:{refused}";
        TurnUsage.RecordEscalation(decision);
        InvestigationLog.Escalation(logger, investigationId, first.Turn.Failure!.Value, decision, timer.ElapsedMilliseconds);
        if (refused is not null)
        {
            return (result, first.Turn.Gaps.Count);
        }

        result = await second.RunAsync(messages, budget, progress, timer, cancellationToken);
        TurnUsage.RecordAttempt(new AttemptRecord(second.Tier, second.Turn.Failure));
        return (result, second.Turn.Gaps.Count);
    }

    private Attempt Prepare(ModelTier tier, IProgress<InvestigationProgress>? progress, Guid investigationId, MerchantSources named, InvestigationBudget budget, ReadCache reads)
    {
        var turn = new TurnState();
        var submit = new SubmitAssessmentFunction(turn, clock.Today, progress);
        var guard = new ToolGuard(turn, _limits, time, user, progress, screen, logger, investigationId, named, trace?.KnowledgeIndex, budget, reads);
        List<AITool> readTools = [.. tools.Where(tool => ToolAuthorization.Allows(user, tool.Name)).Select(guard.Wrap)];
        var instructions = fixedPrompt.Instructions();

        // No default tools: tools passed per run are added to an agent's defaults, and the correction run must offer only submit_assessment.
        var agent = new ChatClientAgent(InvestigationPipeline.FunctionInvoking(new BudgetedChatClient(models.For(tier), budget), _limits), new ChatClientAgentOptions
        {
            Name = "ops-investigation",
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                MaxOutputTokens = _limits.MaxOutputTokens,
                AllowMultipleToolCalls = false,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });

        return new Attempt(this, tier, turn, agent, [.. readTools, submit], submit, investigationId, new ChatOptions { Instructions = instructions, Tools = [.. readTools, submit] });
    }

    private sealed record Attempt(
        InvestigationAgentRunner Runner, ModelTier Tier, TurnState Turn, ChatClientAgent Agent, IList<AITool> Tools, SubmitAssessmentFunction Submit, Guid InvestigationId, ChatOptions FirstRequestOptions)
    {
        public async Task<AssessmentResult> RunAsync(List<ChatMessage> messages, InvestigationBudget budget, IProgress<InvestigationProgress>? progress, Stopwatch timer, CancellationToken cancellationToken)
        {
            // One span per attempt under the investigation's, naming the tier and the deployment it called.
            using var span = AgentTelemetry.Source.StartActivity("attempt");
            span?.SetTag("paymentops.attempt.tier", ToolJson.Word(Tier));
            span?.SetTag("paymentops.attempt.deployment", Runner.Models.For(Tier).GetService<ChatClientMetadata>()?.DefaultModelId);
            budget.StartAttempt();
            var logger = Runner.Logger;
            var result = await TurnOutcome.RunAsync(
                Turn,
                budget,
                progress,
                async token =>
                {
                    // A fresh session every attempt: earlier turns arrive only as the plain-text history, never as tool messages.
                    var session = await Agent.CreateSessionAsync(token);
                    var first = await Agent.RunAsync([.. messages], session, Run(Tools, null), token);

                    // A call no tool answered (an unknown tool) ends the attempt: fail closed, and that history can't be sent again.
                    if (Turn.Accepted is null && HasUnansweredCall(first.Messages))
                    {
                        Turn.Fail(AttemptFailure.UnknownToolCall);
                        return Unverified;
                    }

                    // One more request either way: to correct a rejected submission, or to submit at all.
                    if (Turn.Accepted is null)
                    {
                        var correcting = Turn.SubmissionRejected;
                        await Agent.RunAsync([], session, Run([Submit], ChatToolMode.RequireSpecific(ToolNames.SubmitAssessment)), token);
                        if (Turn.Accepted is null)
                        {
                            Turn.Fail(correcting ? AttemptFailure.RejectedAfterCorrection : AttemptFailure.NoSubmission);
                        }
                    }

                    return null;
                },
                (failure, ex) =>
                {
                    switch (failure)
                    {
                        case TurnFailure.ServiceFailed:
                            InvestigationLog.ModelFailed(logger, InvestigationId, ex, timer.ElapsedMilliseconds);
                            break;
                        case TurnFailure.Blocked:
                            InvestigationLog.Blocked(logger, InvestigationId, ex is ContentFilteredException ? "completion" : "prompt", timer.ElapsedMilliseconds);
                            break;
                        default:
                            InvestigationLog.Aborted(logger, InvestigationId, ex, timer.ElapsedMilliseconds);
                            break;
                    }
                },
                cancellationToken);
            span?.SetTag("paymentops.attempt.failure", Turn.Failure is { } failure ? ToolJson.Word(failure) : "none");
            return result;
        }
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
