namespace PaymentOps.Domain.Merchants;

/// <summary>A merchant and the processors its payments are routed to.</summary>
public sealed record Merchant(string Id, string Name, IReadOnlyList<string> ProcessorIds, RiskHold? RiskHold);

/// <summary>A pause on the merchant's payouts. <paramref name="RuleId"/> is null for a manual hold.</summary>
public sealed record RiskHold(string Id, DateTimeOffset PlacedAt, string? RuleId, DateOnly ReviewBy);
