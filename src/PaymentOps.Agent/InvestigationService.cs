using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;

namespace PaymentOps.Agent;

/// <summary>Grounded answers from knowledge, and investigations with the agent, behind one seam.</summary>
internal sealed class InvestigationService(GroundedAnswerService answers, InvestigationAgentRunner agent) : IInvestigationService
{
    public Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken) => answers.AskAsync(question, cancellationToken);

    public Task<AssessmentResult> InvestigateAsync(string question, CancellationToken cancellationToken) => agent.InvestigateAsync(question, cancellationToken);

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => answers.GetSourceAsync(sectionKey, cancellationToken);
}
