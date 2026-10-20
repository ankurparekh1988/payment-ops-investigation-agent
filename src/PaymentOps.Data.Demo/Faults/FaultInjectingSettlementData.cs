using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingSettlementData(ISettlementData inner, FaultOptions options) : ISettlementData
{
    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(string merchantId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        Run(() => inner.GetPaymentsAsync(merchantId, fromInclusive, toExclusive, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SettlementFile>> GetFilesAsync(string processorId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Run(() => inner.GetFilesAsync(processorId, fromInclusive, toExclusive, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<Payout>> GetPayoutsAsync(string merchantId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Run(() => inner.GetPayoutsAsync(merchantId, fromInclusive, toExclusive, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SettlementDay>> GetCalendarAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken) =>
        Run(() => inner.GetCalendarAsync(fromInclusive, toExclusive, cancellationToken), cancellationToken);

    private Task<IReadOnlyList<T>> Run<T>(Func<Task<IReadOnlyList<T>>> call, CancellationToken cancellationToken) =>
        FaultInjector.RunAsync<IReadOnlyList<T>>(options, DemoDataPorts.Settlement, call, [], cancellationToken);
}
