using System.ClientModel;
using System.Diagnostics;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenAI.Chat;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

using NotAnswered = PaymentOps.Agent.Contracts.InvestigationResult.NotAnswered;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// One question, one answer: authorized retrieval, then a model answer that is shown only if every
/// citation checks out against this turn's evidence. Expected failures are results, not exceptions.
/// </summary>
internal sealed class GroundedAnswerService(
    IKnowledgeLookup knowledge,
    IChatClient chat,
    IPromptProvider prompts,
    AiRelease release,
    IUserContext user,
    IOptions<AnsweringOptions> options,
    ILogger<GroundedAnswerService> logger) : IInvestigationService
{
    private readonly AnsweringOptions _options = options.Value;

    public async Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > _options.MaxQuestionLength)
        {
            return new NotAnswered(NotAnsweredReason.InvalidQuestion);
        }

        if (Access() is not { } access)
        {
            return new NotAnswered(NotAnsweredReason.NotAuthorized);
        }

        var retrievalTimer = Stopwatch.StartNew();
        IReadOnlyList<RetrievedSection> sections;
        using (var searchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            searchTimeout.CancelAfter(_options.SearchTimeout);
            try
            {
                sections = await knowledge.RetrieveAsync(question, access, searchTimeout.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                GroundedAnswerLog.DependencyFailed(logger, NotAnsweredReason.KnowledgeUnavailable, "Search", "timeout", null, null, null, ex);
                return new NotAnswered(NotAnsweredReason.KnowledgeUnavailable);
            }
            catch (KnowledgeUnavailableException ex)
            {
                GroundedAnswerLog.DependencyFailed(logger, NotAnsweredReason.KnowledgeUnavailable, "Search", FailureClass(ex.Status), ex.Status, ex.ErrorCode, ex.RequestId, ex);
                return new NotAnswered(NotAnsweredReason.KnowledgeUnavailable);
            }
        }

        retrievalTimer.Stop();
        if (sections.Count == 0)
        {
            GroundedAnswerLog.Completed(logger, new NotAnswered(NotAnsweredReason.NoEvidence), release.RetrievalMode, sections, retrievalTimer.ElapsedMilliseconds, null, null);
            return new NotAnswered(NotAnsweredReason.NoEvidence);
        }

        var registry = EvidenceRegistry.From(sections);
        var messages = GroundedAnswerPrompt.Build(prompts.GetPrompt(GroundedAnswerPrompt.PromptId), question, registry);
        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = _options.MaxOutputTokens,

            // Constrained synthesis over at most five sections: no hidden reasoning eating the output budget.
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },

            // The strict schema is set on the wire request directly; the adapter's own strict switch isn't public.
            RawRepresentationFactory = _ => new ChatCompletionOptions { ResponseFormat = GroundedAnswerSchema.Format },
        };

        var modelTimer = Stopwatch.StartNew();
        ChatResponse response;
        using (var modelTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            modelTimeout.CancelAfter(_options.ModelTimeout);
            try
            {
                response = await chat.GetResponseAsync(messages, chatOptions, modelTimeout.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                GroundedAnswerLog.DependencyFailed(logger, NotAnsweredReason.AnswerUnavailable, "Model", "timeout", null, null, null, ex);
                return new NotAnswered(NotAnsweredReason.AnswerUnavailable);
            }

            // Expected service failures only; anything else is a bug and stays an exception.
            catch (Exception ex) when (ex is ClientResultException or HttpRequestException)
            {
                var blocked = ModelFailure.IsContentFilterRejection(ex);
                var reason = blocked ? NotAnsweredReason.AnswerBlocked : NotAnsweredReason.AnswerUnavailable;
                var client = ex as ClientResultException;
                GroundedAnswerLog.DependencyFailed(logger, reason, "Model", blocked ? "content filter" : FailureClass(client?.Status),
                    client?.Status, client is null ? null : ModelFailure.ErrorCode(client), client is null ? null : ModelFailure.RequestId(client), ex);
                return new NotAnswered(reason);
            }
        }

        modelTimer.Stop();
        var result = GroundedAnswerValidator.Validate(response, registry);
        GroundedAnswerLog.Completed(logger, result, release.RetrievalMode, sections, retrievalTimer.ElapsedMilliseconds, modelTimer.ElapsedMilliseconds, response.Usage);
        return result;
    }

    public async Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken)
    {
        // Same answer as a missing section: no other authorization state is revealed.
        if (Access() is not { } access)
        {
            return new SourceResult.NotFound();
        }

        try
        {
            return await knowledge.FindAsync(sectionKey, access, cancellationToken) is { } section
                ? new SourceResult.Found(section.Title, section.SectionPath, section.Content)
                : new SourceResult.NotFound();
        }
        catch (KnowledgeUnavailableException ex)
        {
            GroundedAnswerLog.DependencyFailed(logger, NotAnsweredReason.KnowledgeUnavailable, "Search", FailureClass(ex.Status), ex.Status, ex.ErrorCode, ex.RequestId, ex);
            return new SourceResult.Unavailable();
        }
    }

    private static string FailureClass(int? status) => status switch
    {
        401 => "authentication",
        403 => "authorization",
        429 => "throttling",
        >= 500 => "server error",
        null => "network",
        _ => "request",
    };

    private KnowledgeAccess? Access()
    {
        try
        {
            return KnowledgeAccess.From(user);
        }
        catch (KnowledgeAccessDeniedException)
        {
            return null;
        }
    }
}
