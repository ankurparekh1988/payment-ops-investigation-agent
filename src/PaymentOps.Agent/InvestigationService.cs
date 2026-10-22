using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;

namespace PaymentOps.Agent;

/// <summary>Investigations with the agent, and the sections they cite, behind one seam.</summary>
internal sealed class InvestigationService(InvestigationAgentRunner agent, SourceService sources) : IInvestigationService
{
    public Task<AssessmentResult> InvestigateAsync(string question, CancellationToken cancellationToken) => agent.InvestigateAsync(question, cancellationToken);

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => sources.GetSourceAsync(sectionKey, cancellationToken);
}
