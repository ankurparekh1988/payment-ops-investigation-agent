using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>
/// The records every port contract runs against. A real adapter's tests load these into its own store;
/// the values sit on range boundaries and share timestamps so ordering and half-open ranges are tested.
/// </summary>
public sealed class ContractData
{
    public static readonly DateTimeOffset AsOf = Utc(2026, 11, 6, 23, 0);

    // Wednesday 4 November is a closure day; the 7th and 8th are a weekend.
    public static readonly DateOnly ClosureDate = new(2026, 11, 4);

    public IReadOnlyList<Processor> Processors { get; } =
    [
        new("p-a", "Processor A", new(new TimeOnly(16, 0), "America/New_York", new TimeOnly(2, 0), TimeSpan.FromHours(2), 0)),
        new("p-b", "Processor B", new(new TimeOnly(17, 0), "Europe/London", new TimeOnly(3, 0), TimeSpan.FromHours(2), 1)),
    ];

    public IReadOnlyList<SettlementDay> ClosureDays { get; } = [new(ClosureDate, false, "Settlement system closure")];

    public IReadOnlyList<Merchant> Merchants { get; } =
    [
        new("M-1", "Merchant One", ["p-a", "p-b"], null),
        new("M-2", "Merchant Two", ["p-a"], null),
        new("M-3", "Merchant Three", ["p-b"], new RiskHold("RH-1", Utc(2026, 11, 1, 9, 0), "RF-37", new DateOnly(2026, 11, 20))),
    ];

    public IReadOnlyList<Payment> Payments { get; } =
    [
        new("PAY-1", "M-1", "p-a", 10m, "USD", Utc(2026, 11, 2, 10, 0), Utc(2026, 11, 2, 10, 0), new(2026, 11, 2)),
        new("PAY-2", "M-1", "p-a", 20m, "USD", Utc(2026, 11, 3, 0, 0), Utc(2026, 11, 3, 0, 0), new(2026, 11, 3)),
        new("PAY-3", "M-1", "p-b", 30m, "GBP", Utc(2026, 11, 3, 0, 0), null, new(2026, 11, 3)),
        new("PAY-4", "M-2", "p-a", 40m, "USD", Utc(2026, 11, 2, 12, 0), Utc(2026, 11, 2, 12, 0), new(2026, 11, 2)),
        new("PAY-5", "M-1", "p-a", 50m, "USD", Utc(2026, 11, 6, 21, 0), null, null),
    ];

    public IReadOnlyList<SettlementFile> Files { get; } =
    [
        new("SF-A2", "p-a", new(2026, 11, 3), Utc(2026, 11, 4, 7, 0), SettlementFileStatus.Accepted, [new("M-1", ["PAY-2"], 20m, "USD")]),
        new("SF-A1", "p-a", new(2026, 11, 2), Utc(2026, 11, 3, 7, 0), SettlementFileStatus.Accepted,
            [new("M-1", ["PAY-1"], 10m, "USD"), new("M-2", ["PAY-4"], 40m, "USD")]),
        new("SF-B1", "p-b", new(2026, 11, 3), Utc(2026, 11, 4, 3, 0), SettlementFileStatus.Accepted, [new("M-1", ["PAY-3"], 30m, "GBP")]),
    ];

    public IReadOnlyList<Payout> Payouts { get; } =
    [
        new("PO-1", "M-1", "p-a", new(2026, 11, 2), ["SF-A1"], new(2026, 11, 3), 10m, "USD", PayoutStatus.Paid, Utc(2026, 11, 3, 12, 0)),
        new("PO-2", "M-1", "p-a", new(2026, 11, 3), ["SF-A2"], new(2026, 11, 5), 20m, "USD", PayoutStatus.Paid, Utc(2026, 11, 5, 12, 0)),
        new("PO-3", "M-1", "p-b", new(2026, 11, 3), ["SF-B1"], new(2026, 11, 9), 30m, "GBP", PayoutStatus.Scheduled, null),
        new("PO-4", "M-2", "p-a", new(2026, 11, 2), ["SF-A1"], new(2026, 11, 3), 40m, "USD", PayoutStatus.Paid, Utc(2026, 11, 3, 12, 0)),
    ];

    public IReadOnlyList<ProcessorIncident> Incidents { get; } =
    [
        new("INC-1", "p-a", ProcessorComponent.Settlement, "Settlement delayed", Utc(2026, 11, 1, 20, 0), null, "PA-1"),
        new("INC-2", "p-a", ProcessorComponent.Authorization, "Elevated declines", Utc(2026, 11, 3, 10, 0), Utc(2026, 11, 3, 12, 0), null),
        new("INC-3", "p-a", ProcessorComponent.FileDelivery, "Late files", Utc(2026, 10, 31, 10, 0), Utc(2026, 11, 2, 0, 0), null),
        new("INC-4", "p-b", ProcessorComponent.Settlement, "Batch closed late", Utc(2026, 11, 3, 9, 0), Utc(2026, 11, 3, 10, 0), null),
    ];

    public IReadOnlyList<ErrorEvent> ErrorEvents { get; } =
    [
        new("EV-4", Utc(2026, 11, 2, 15, 0), "p-a", "M-2", ErrorCategory.FileTransferTimeout, "SFTP-TIMEOUT", null),
        new("EV-1", Utc(2026, 11, 2, 15, 0), "p-a", "M-1", ErrorCategory.FileTransferTimeout, "SFTP-TIMEOUT", null),
        new("EV-2", Utc(2026, 11, 3, 0, 0), "p-a", null, ErrorCategory.AcknowledgementFailed, "ACK-FAILED", "SF-A1"),
        new("EV-3", Utc(2026, 11, 3, 1, 0), "p-b", "M-1", ErrorCategory.FileRejected, "FILE-REJECTED", null),
    ];

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    public static CancellationToken Cancelled() => new(canceled: true);
}
