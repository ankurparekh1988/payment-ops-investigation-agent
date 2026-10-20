using PaymentOps.Domain.ErrorEvents;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingErrorEventData(IErrorEventData inner, FaultOptions options) : IErrorEventData
{
    public Task<IReadOnlyList<ErrorEvent>> GetEventsAsync(ErrorEventQuery query, CancellationToken cancellationToken) =>
        FaultInjector.RunAsync<IReadOnlyList<ErrorEvent>>(options, DemoDataPorts.ErrorEvents, () => inner.GetEventsAsync(query, cancellationToken), [], cancellationToken);
}
