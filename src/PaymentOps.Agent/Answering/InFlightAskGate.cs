using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// One question at a time per instance. Registered scoped, so in Blazor Server that's one per
/// circuit; the disabled button is only the visible half. Per-user quotas are a later concern.
/// </summary>
internal sealed class InFlightAskGate(IInvestigationService inner) : IInvestigationService
{
    private int _inFlight;

    public async Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return new InvestigationResult.NotAnswered(NotAnsweredReason.Busy);
        }

        try
        {
            return await inner.AskAsync(question, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        inner.GetSourceAsync(sectionKey, cancellationToken);
}
