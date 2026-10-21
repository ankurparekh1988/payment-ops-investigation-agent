namespace PaymentOps.Domain.Settlement;

/// <summary>
/// The settlement system of record. Ranges are half-open, [fromInclusive, toExclusive), and fromInclusive must be before toExclusive; there's
/// no maximum span. Results are ordered by their date or time, then by id.
/// </summary>
public interface ISettlementData
{
    /// <summary>The merchant's payments captured (our capture time) in the range.</summary>
    Task<IReadOnlyList<Payment>> GetPaymentsAsync(string merchantId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken);

    /// <summary>The processor's settlement files with a batch date in the range.</summary>
    Task<IReadOnlyList<SettlementFile>> GetFilesAsync(string processorId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken);

    /// <summary>The merchant's payouts with a batch date in the range.</summary>
    Task<IReadOnlyList<Payout>> GetPayoutsAsync(string merchantId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken);

    /// <summary>Every date in the range, saying whether it's a settlement day.</summary>
    Task<IReadOnlyList<SettlementDay>> GetCalendarAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken);
}
