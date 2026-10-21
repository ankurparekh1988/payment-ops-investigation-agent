using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.ErrorEvents;

namespace PaymentOps.Data.Demo;

internal sealed class DemoErrorEventData(DemoDataset dataset) : IErrorEventData
{
    public Task<IReadOnlyList<ErrorEvent>> GetEventsAsync(ErrorEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ProcessorId is null && query.MerchantId is null)
        {
            // Unscoped, this would be every event the platform recorded in the range.
            throw new ArgumentException("An error event query needs a processor, a merchant, or both.", nameof(query));
        }

        HalfOpenRange.Require(query.FromInclusive, query.ToExclusive);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<ErrorEvent>>([.. dataset.ErrorEvents
            .Where(e => (query.ProcessorId is null || e.ProcessorId == query.ProcessorId)
                && (query.MerchantId is null || e.MerchantId == query.MerchantId)
                && HalfOpenRange.Contains(e.OccurredAt, query.FromInclusive, query.ToExclusive))
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id, StringComparer.Ordinal)]);
    }
}
