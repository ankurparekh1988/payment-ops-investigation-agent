using PaymentOps.Domain.Processors;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingProcessorData(IProcessorData inner, FaultOptions options) : IProcessorData
{
    public Task<Processor?> FindAsync(string processorId, CancellationToken cancellationToken) =>
        FaultInjector.RunAsync(options, DemoDataPorts.Processors, () => inner.FindAsync(processorId, cancellationToken), null, cancellationToken);

    public Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        FaultInjector.RunAsync<IReadOnlyList<ProcessorIncident>>(
            options, DemoDataPorts.Processors, () => inner.GetIncidentsAsync(processorId, fromInclusive, toExclusive, cancellationToken), [], cancellationToken);
}
