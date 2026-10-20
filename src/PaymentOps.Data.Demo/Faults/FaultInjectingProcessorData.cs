using PaymentOps.Domain.Processors;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingProcessorData(IProcessorData inner, FaultInjector faults) : IProcessorData
{
    public Task<Processor?> FindAsync(string processorId, CancellationToken cancellationToken) =>
        faults.RunAsync(() => inner.FindAsync(processorId, cancellationToken), null, cancellationToken);

    public Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        faults.RunAsync<IReadOnlyList<ProcessorIncident>>(() => inner.GetIncidentsAsync(processorId, fromInclusive, toExclusive, cancellationToken), [], cancellationToken);
}
