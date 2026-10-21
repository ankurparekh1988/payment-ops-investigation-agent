using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// One question or investigation at a time per instance. Registered scoped, so in Blazor Server that's one per
/// circuit; the disabled button is only the visible half. Per-user quotas are a later concern.
/// </summary>
internal sealed class InFlightAskGate(IInvestigationService inner) : IInvestigationService
{
    private int _inFlight;

    public Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken) =>
        GatedAsync(() => inner.AskAsync(question, cancellationToken), new InvestigationResult.NotAnswered(NotAnsweredReason.Busy));

    public Task<AssessmentResult> InvestigateAsync(string question, CancellationToken cancellationToken) =>
        GatedAsync(() => inner.InvestigateAsync(question, cancellationToken), new AssessmentResult.NotAssessed(NotAssessedReason.Busy));

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        inner.GetSourceAsync(sectionKey, cancellationToken);

    private async Task<T> GatedAsync<T>(Func<Task<T>> run, T busy)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return busy;
        }

        try
        {
            return await run();
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }
}
