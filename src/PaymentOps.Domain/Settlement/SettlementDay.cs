namespace PaymentOps.Domain.Settlement;

/// <summary>One calendar date. Weekends and closure days aren't settlement days; only a closure has a reason.</summary>
public sealed record SettlementDay(DateOnly Date, bool IsSettlementDay, string? ClosureReason);
