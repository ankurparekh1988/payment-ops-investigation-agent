using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>
/// One synthetic system-of-record snapshot. Investigations are reasoned about as of <paramref name="AsOf"/>,
/// so the demo's dates stay fixed however long ago the dataset was written.
/// </summary>
internal sealed record DemoDataset(
    DateTimeOffset AsOf,
    IReadOnlyList<Processor> Processors,
    IReadOnlyList<ClosureDay> ClosureDays,
    IReadOnlyList<Merchant> Merchants,
    IReadOnlyList<Payment> Payments,
    IReadOnlyList<SettlementFile> Files,
    IReadOnlyList<Payout> Payouts,
    IReadOnlyList<ProcessorIncident> Incidents,
    IReadOnlyList<ErrorEvent> ErrorEvents);

/// <summary>A weekday with no settlement. Weekends aren't listed; they're never settlement days.</summary>
internal sealed record ClosureDay(DateOnly Date, string Reason);
