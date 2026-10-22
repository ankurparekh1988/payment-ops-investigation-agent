using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web;

/// <summary>Development without Azure endpoints: every investigation reports the model as unavailable.</summary>
internal sealed class UnconfiguredInvestigationService : IInvestigationService
{
    public Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult<AssessmentResult>(new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable));

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        Task.FromResult<SourceResult>(new SourceResult.Unavailable());
}
