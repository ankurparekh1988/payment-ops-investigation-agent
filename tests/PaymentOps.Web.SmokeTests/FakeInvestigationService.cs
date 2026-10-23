using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.SmokeTests;

internal sealed class FakeInvestigationService(AssessmentResult? result = null, Func<string, SourceResult>? source = null) : IInvestigationService
{
    public Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(result ?? new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable));

    public ConversationStatus Conversation { get; } = new(0, 0, false);

    public void StartNew()
    {
    }

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        Task.FromResult(source?.Invoke(sectionKey) ?? new SourceResult.NotFound());
}
