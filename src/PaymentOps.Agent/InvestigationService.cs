using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Domain.Security;

namespace PaymentOps.Agent;

/// <summary>Investigations with the agent within one conversation, and the sections they cite, behind one seam.</summary>
internal sealed class InvestigationService(
    InvestigationAgentRunner agent,
    FixedPrompt fixedPrompt,
    SourceService sources,
    Conversation conversation,
    IOptions<ConversationOptions> conversationOptions,
    IOptions<InvestigationLimits> limits,
    IUserContext user,
    ProposalStore proposals) : IInvestigationService
{
    public ConversationStatus Conversation
    {
        get
        {
            var (history, ended) = Plan();
            return new ConversationStatus(conversation.Turns.Count, history.Count / 2, ended);
        }
    }

    public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        var investigationId = Guid.NewGuid();

        // The page replaces the card with each new question, so a proposal nobody can see any longer stops waiting.
        proposals.Supersede(conversation.Id);
        var (history, ended) = Plan();
        if (ended)
        {
            return new AssessmentResult.NotAssessed(NotAssessedReason.ConversationLimit) { InvestigationId = investigationId };
        }

        var result = await agent.InvestigateAsync(question, history, progress, investigationId, cancellationToken);

        // A cancel that lands after the agent finished still keeps nothing: no proposal, no turn.
        if (cancellationToken.IsCancellationRequested)
        {
            return new AssessmentResult.NotAssessed(NotAssessedReason.Cancelled) { InvestigationId = investigationId };
        }

        if (result is AssessmentResult.Assessed { Proposal: { } draft } assessed && user.IsInRole(OpsRoles.Engineer))
        {
            // Only an Engineer can approve, so only an Engineer's proposal is kept; a Reader's stays a draft on the page.
            result = assessed with
            {
                Proposal = proposals.Open(draft, investigationId, conversation.Id, user.UserId) ?? draft with { Status = ProposalStatus.Unavailable },
            };
        }

        if (result is not AssessmentResult.NotAssessed { Reason: NotAssessedReason.Cancelled or NotAssessedReason.InvalidQuestion or NotAssessedReason.NotAuthorized })
        {
            conversation.Add(new ConversationTurn(question, result));
        }

        return result;
    }

    public void StartNew()
    {
        proposals.Supersede(conversation.Id);
        conversation.Reset();
    }

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => sources.GetSourceAsync(sectionKey, cancellationToken);

    // The history the next question would carry, and whether there can be a next question at all.
    private (IReadOnlyList<ChatMessage> History, bool Ended) Plan()
    {
        var turns = conversation.Turns;
        var budget = HistoryProjection.HistoryBudgetChars(conversationOptions.Value, limits.Value, fixedPrompt.Chars);
        var history = HistoryProjection.Build(turns, budget, proposals.NoteFor);
        // Nothing carried while an answered turn exists means the newest answered turn alone doesn't fit.
        var ended = turns.Count >= conversationOptions.Value.MaxQuestions || (history.Count == 0 && turns.Any(t => HistoryProjection.IsCarried(t.Result)));
        return (history, ended);
    }
}
