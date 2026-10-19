using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web;

/// <summary>Development without Azure endpoints: every question reports the knowledge service as unavailable.</summary>
internal sealed class UnconfiguredInvestigationService : IInvestigationService
{
    public Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken) =>
        Task.FromResult<InvestigationResult>(new InvestigationResult.NotAnswered(NotAnsweredReason.KnowledgeUnavailable));

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        Task.FromResult<SourceResult>(new SourceResult.Unavailable());
}
