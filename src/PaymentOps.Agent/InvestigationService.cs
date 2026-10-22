using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;

namespace PaymentOps.Agent;

/// <summary>Investigations with the agent within one conversation, and the sections they cite, behind one seam.</summary>
internal sealed class InvestigationService(
    InvestigationAgentRunner agent,
    SourceService sources,
    Conversation conversation,
    IOptions<ConversationOptions> conversationOptions,
    IOptions<InvestigationLimits> limits) : IInvestigationService
{
    public ConversationStatus Conversation
    {
        get
        {
            var (carried, ended) = Plan();
            return new ConversationStatus(conversation.Turns.Count, carried.Carried, ended);
        }
    }

    public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        var investigationId = Guid.NewGuid();
        var (history, ended) = Plan();
        if (ended)
        {
            return new AssessmentResult.NotAssessed(NotAssessedReason.ConversationLimit) { InvestigationId = investigationId };
        }

        var result = await agent.InvestigateAsync(question, history.Messages, progress, investigationId, cancellationToken);
        if (result is not AssessmentResult.NotAssessed { Reason: NotAssessedReason.Cancelled or NotAssessedReason.InvalidQuestion or NotAssessedReason.NotAuthorized })
        {
            conversation.Add(new ConversationTurn(question, result));
        }

        return result;
    }

    public void StartNew() => conversation.Reset();

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => sources.GetSourceAsync(sectionKey, cancellationToken);

    // The history the next question would carry, and whether there can be a next question at all.
    private ((IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> Messages, int Carried) History, bool Ended) Plan()
    {
        var turns = conversation.Turns;
        var budget = HistoryProjection.HistoryBudgetChars(conversationOptions.Value, limits.Value, agent.FixedPromptChars());
        var history = HistoryProjection.Build(turns, budget, _ => null);
        var ended = turns.Count >= conversationOptions.Value.MaxQuestions || (turns.Count > 0 && history.Carried == 0);
        return (history, ended);
    }
}
