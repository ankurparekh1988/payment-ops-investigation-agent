using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.Processors;

namespace PaymentOps.Data.Demo;

internal sealed class DemoProcessorData(DemoDataset dataset) : IProcessorData
{
    // Ids are unique: the loader checked them.
    public Task<Processor?> FindAsync(string processorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processorId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(dataset.Processors.FirstOrDefault(p => p.Id == processorId));
    }

    // An incident overlaps the window if it started before the window ends and wasn't resolved by its start.
    public Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processorId);
        HalfOpenRange.Require(fromInclusive, toExclusive);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<ProcessorIncident>>([.. dataset.Incidents
            .Where(i => i.ProcessorId == processorId && i.StartedAt < toExclusive && (i.ResolvedAt is null || i.ResolvedAt > fromInclusive))
            .OrderBy(i => i.StartedAt).ThenBy(i => i.Id, StringComparer.Ordinal)]);
    }
}
