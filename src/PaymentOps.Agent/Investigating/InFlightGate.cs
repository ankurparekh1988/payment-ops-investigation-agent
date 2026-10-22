using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// One investigation at a time per instance. Registered scoped, so in Blazor Server that's one per
/// circuit; the disabled button is only the visible half. Per-user quotas are a later concern.
/// </summary>
internal sealed class InFlightGate(IInvestigationService inner) : IInvestigationService
{
    private int _inFlight;

    public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return new AssessmentResult.NotAssessed(NotAssessedReason.Busy);
        }

        try
        {
            return await inner.InvestigateAsync(question, progress, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        inner.GetSourceAsync(sectionKey, cancellationToken);
}
