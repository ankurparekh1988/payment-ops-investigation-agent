using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingSettlementData(ISettlementData inner, FaultInjector faults) : ISettlementData
{
    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(string merchantId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        faults.RunAsync<IReadOnlyList<Payment>>(() => inner.GetPaymentsAsync(merchantId, fromInclusive, toExclusive, cancellationToken), [], cancellationToken);

    public Task<IReadOnlyList<SettlementFile>> GetFilesAsync(string processorId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        faults.RunAsync<IReadOnlyList<SettlementFile>>(() => inner.GetFilesAsync(processorId, fromInclusive, toExclusive, cancellationToken), [], cancellationToken);

    public Task<IReadOnlyList<Payout>> GetPayoutsAsync(string merchantId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        faults.RunAsync<IReadOnlyList<Payout>>(() => inner.GetPayoutsAsync(merchantId, fromInclusive, toExclusive, cancellationToken), [], cancellationToken);

    public Task<IReadOnlyList<SettlementDay>> GetCalendarAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        faults.RunAsync<IReadOnlyList<SettlementDay>>(() => inner.GetCalendarAsync(fromInclusive, toExclusive, cancellationToken), [], cancellationToken);
}
