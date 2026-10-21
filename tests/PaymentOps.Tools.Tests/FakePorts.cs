using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Tools.Tests;

/// <summary>
/// Hand-built ports for cases the shipped dataset can't show: a processor west of New York, or a merchant
/// routed to a processor with no record.
/// </summary>
internal sealed class FakePorts(DateTimeOffset now) : IMerchantData, ISettlementData, IProcessorData, IInvestigationClock
{
    public List<Merchant> Merchants { get; } = [];

    public List<Processor> Processors { get; } = [];

    public List<Payment> Payments { get; } = [];

    public List<SettlementFile> Files { get; } = [];

    public DateTimeOffset Now { get; } = now;

    public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public Task<Merchant?> FindAsync(string merchantId, CancellationToken cancellationToken) =>
        Task.FromResult(Merchants.Find(m => m.Id == merchantId));

    Task<Processor?> IProcessorData.FindAsync(string processorId, CancellationToken cancellationToken) =>
        Task.FromResult(Processors.Find(p => p.Id == processorId));

    public Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessorIncident>>([]);

    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(string merchantId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payment>>([.. Payments.Where(p => p.MerchantId == merchantId && p.CapturedAt >= fromInclusive && p.CapturedAt < toExclusive)]);

    public Task<IReadOnlyList<SettlementFile>> GetFilesAsync(string processorId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettlementFile>>([.. Files.Where(f => f.ProcessorId == processorId && f.BatchDate >= fromInclusive && f.BatchDate < toExclusive)]);

    public Task<IReadOnlyList<Payout>> GetPayoutsAsync(string merchantId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payout>>([]);

    // Weekends only; enough for these cases.
    public Task<IReadOnlyList<SettlementDay>> GetCalendarAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettlementDay>>([.. Enumerable.Range(0, toExclusive.DayNumber - fromInclusive.DayNumber)
            .Select(fromInclusive.AddDays)
            .Select(d => new SettlementDay(d, d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), null))]);
}
