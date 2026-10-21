namespace PaymentOps.Domain.ErrorEvents;

/// <summary>Events in [FromInclusive, ToExclusive) for a processor, a merchant, or both; at least one is required.</summary>
public sealed record ErrorEventQuery(string? ProcessorId, string? MerchantId, DateTimeOffset FromInclusive, DateTimeOffset ToExclusive);
