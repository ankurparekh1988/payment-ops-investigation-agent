using PaymentOps.Domain.ErrorEvents;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingErrorEventData(IErrorEventData inner, FaultInjector faults) : IErrorEventData
{
    public Task<IReadOnlyList<ErrorEvent>> GetEventsAsync(ErrorEventQuery query, CancellationToken cancellationToken) =>
        faults.RunAsync<IReadOnlyList<ErrorEvent>>(() => inner.GetEventsAsync(query, cancellationToken), [], cancellationToken);
}
