namespace PaymentOps.Domain.Settlement;

/// <summary>
/// A payout to a merchant from one batch. <paramref name="ScheduledFor"/> is the settlement day it's due;
/// <paramref name="SourceFileIds"/> are the files it's paid from, more than one when a batch was split.
/// </summary>
public sealed record Payout(
    string Id,
    string MerchantId,
    string ProcessorId,
    DateOnly BatchDate,
    IReadOnlyList<string> SourceFileIds,
    DateOnly ScheduledFor,
    decimal Amount,
    string Currency,
    PayoutStatus Status,
    DateTimeOffset? PaidAt);

public enum PayoutStatus
{
    Scheduled,
    Paid,
    Held,
}
