using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo;

internal sealed class DemoSettlementData(DemoDataset dataset) : ISettlementData
{
    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(string merchantId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantId);
        HalfOpenRange.Require(fromInclusive, toExclusive);
        cancellationToken.ThrowIfCancellationRequested();

        return Result(dataset.Payments
            .Where(p => p.MerchantId == merchantId && HalfOpenRange.Contains(p.CapturedAt, fromInclusive, toExclusive))
            .OrderBy(p => p.CapturedAt).ThenBy(p => p.Id, StringComparer.Ordinal));
    }

    public Task<IReadOnlyList<SettlementFile>> GetFilesAsync(string processorId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processorId);
        HalfOpenRange.Require(fromInclusive, toExclusive);
        cancellationToken.ThrowIfCancellationRequested();

        return Result(dataset.Files
            .Where(f => f.ProcessorId == processorId && HalfOpenRange.Contains(f.BatchDate, fromInclusive, toExclusive))
            .OrderBy(f => f.BatchDate).ThenBy(f => f.Id, StringComparer.Ordinal));
    }

    public Task<IReadOnlyList<Payout>> GetPayoutsAsync(string merchantId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantId);
        HalfOpenRange.Require(fromInclusive, toExclusive);
        cancellationToken.ThrowIfCancellationRequested();

        return Result(dataset.Payouts
            .Where(p => p.MerchantId == merchantId && HalfOpenRange.Contains(p.BatchDate, fromInclusive, toExclusive))
            .OrderBy(p => p.BatchDate).ThenBy(p => p.Id, StringComparer.Ordinal));
    }

    // Weekends are computed and closure days come from the dataset, so any range is answered in full.
    public Task<IReadOnlyList<SettlementDay>> GetCalendarAsync(DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        HalfOpenRange.Require(fromInclusive, toExclusive);
        cancellationToken.ThrowIfCancellationRequested();

        var closures = dataset.ClosureDays.ToDictionary(c => c.Date, c => c.Reason);
        var days = Enumerable.Range(0, toExclusive.DayNumber - fromInclusive.DayNumber)
            .Select(fromInclusive.AddDays)
            .Select(date => closures.TryGetValue(date, out var reason)
                ? new SettlementDay(date, false, reason)
                : new SettlementDay(date, date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), null));
        return Result(days);
    }

    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>([.. items]);
}
