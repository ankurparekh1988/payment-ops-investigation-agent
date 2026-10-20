namespace PaymentOps.Domain.Processors;

/// <summary>
/// Processor configuration and the status incidents processors report. Ranges are half-open, [from, to),
/// and from must be before to; there's no maximum span. Results are ordered by start time, then id.
/// </summary>
public interface IProcessorData
{
    /// <summary>The processor, or null if there's no processor with that id.</summary>
    Task<Processor?> FindAsync(string processorId, CancellationToken cancellationToken);

    /// <summary>The processor's incidents that overlap the range, including ones still open.</summary>
    Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken);
}
