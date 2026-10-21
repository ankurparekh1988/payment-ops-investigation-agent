using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.SmokeTests;

internal sealed class FakeInvestigationService(InvestigationResult answer, Func<string, SourceResult>? source = null) : IInvestigationService
{
    public Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken) => Task.FromResult(answer);

    public Task<AssessmentResult> InvestigateAsync(string question, CancellationToken cancellationToken) =>
        Task.FromResult<AssessmentResult>(new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable));

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        Task.FromResult(source?.Invoke(sectionKey) ?? new SourceResult.NotFound());
}
