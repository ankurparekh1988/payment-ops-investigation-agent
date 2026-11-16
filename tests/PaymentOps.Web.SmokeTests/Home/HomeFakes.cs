using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Web.SmokeTests.Home;

/// <summary>
/// The overview's sources, holding a small fixed set of records and counting what was asked of them, so a test can
/// tell whether a section was fetched at all.
/// </summary>
internal sealed class OverviewPorts : IProcessorData, ISettlementData, IIncidentStore, IInvestigationClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 11, 6, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public List<(DateTimeOffset From, DateTimeOffset To)> IncidentWindows { get; } = [];

    public List<(DateOnly From, DateOnly To)> AttentionWindows { get; } = [];

    public int TicketReads { get; private set; }

    public List<ProcessorIncident> Incidents { get; } =
    [
        new("INC-FAB-1109", "fabrikam-pay", ProcessorComponent.Settlement, "Settlement batch delayed", new(2026, 11, 9, 18, 30, 0, TimeSpan.Zero), null, "FAB-7731"),
    ];

    public List<Payout> Held { get; } =
    [
        new("PO-1", "M-2087", "northwind-acquiring", new(2026, 11, 4), ["SF-1"], new(2026, 11, 5), 10m, "USD", PayoutStatus.Held, null),
        new("PO-2", "M-2087", "northwind-acquiring", new(2026, 11, 2), ["SF-1"], new(2026, 11, 3), 10m, "USD", PayoutStatus.Held, null),
    ];

    public List<SettlementFile> Rejected { get; } =
    [
        new("SF-FAB-1104-X", "fabrikam-pay", new(2026, 11, 4), new(2026, 11, 5, 7, 0, 0, TimeSpan.Zero), SettlementFileStatus.Rejected, []),
    ];

    public List<IncidentTicket> Tickets { get; } = [];

    public Task<Processor?> FindAsync(string processorId, CancellationToken cancellationToken) =>
        Task.FromResult<Processor?>(processorId switch
        {
            "fabrikam-pay" => new Processor("fabrikam-pay", "Fabrikam Pay", null!),
            "northwind-acquiring" => new Processor("northwind-acquiring", "Northwind Acquiring", null!),
            _ => null,
        });

    public Task<IReadOnlyList<ProcessorIncident>> GetAllIncidentsAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        IncidentWindows.Add((fromInclusive, toExclusive));
        return Task.FromResult<IReadOnlyList<ProcessorIncident>>(
            [.. Incidents.Where(i => i.StartedAt < toExclusive && (i.ResolvedAt is null || i.ResolvedAt > fromInclusive))]);
    }

    public Task<IReadOnlyList<Payout>> GetHeldPayoutsAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        AttentionWindows.Add((fromInclusive, toExclusive));
        return Task.FromResult<IReadOnlyList<Payout>>([.. Held.Where(p => p.BatchDate >= fromInclusive && p.BatchDate < toExclusive)]);
    }

    public Task<IReadOnlyList<SettlementFile>> GetRejectedFilesAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettlementFile>>([.. Rejected.Where(f => f.BatchDate >= fromInclusive && f.BatchDate < toExclusive)]);

    public Task<IReadOnlyList<IncidentTicket>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        TicketReads++;
        return Task.FromResult<IReadOnlyList<IncidentTicket>>([.. Tickets.OrderByDescending(t => t.CreatedAt).Take(count)]);
    }

    // The overview reads nothing else.
    public Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(string merchantId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SettlementFile>> GetFilesAsync(string processorId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Payout>> GetPayoutsAsync(string merchantId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SettlementDay>> GetCalendarAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IncidentTicket> CreateAsync(string idempotencyKey, IncidentTicketContent content, CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<IncidentTicket?> IIncidentStore.FindAsync(string ticketId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
