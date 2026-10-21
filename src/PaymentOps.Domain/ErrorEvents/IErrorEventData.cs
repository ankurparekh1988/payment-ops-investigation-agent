namespace PaymentOps.Domain.ErrorEvents;

/// <summary>Our error events. There's no maximum span; results are ordered by time, then id.</summary>
public interface IErrorEventData
{
    Task<IReadOnlyList<ErrorEvent>> GetEventsAsync(ErrorEventQuery query, CancellationToken cancellationToken);
}
