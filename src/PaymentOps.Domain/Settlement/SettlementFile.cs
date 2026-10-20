namespace PaymentOps.Domain.Settlement;

/// <summary>A settlement file received from a processor. A batch with no file has no record.</summary>
public sealed record SettlementFile(
    string Id,
    string ProcessorId,
    DateOnly BatchDate,
    DateTimeOffset ReceivedAt,
    SettlementFileStatus Status,
    IReadOnlyList<SettlementLine> Lines);

/// <summary>One merchant's share of a settlement file.</summary>
public sealed record SettlementLine(string MerchantId, IReadOnlyList<string> PaymentIds, decimal Amount, string Currency);

public enum SettlementFileStatus
{
    Accepted,
    Rejected,
}
