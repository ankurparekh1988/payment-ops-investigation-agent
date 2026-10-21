using System.Globalization;

namespace PaymentOps.Tools;

/// <summary>Something a tool returned that an assessment may rest on. Ids are built here, never by the model.</summary>
public abstract record EvidenceItem(string Id, string Label);

/// <summary>A record from a system of record, such as <c>payout:PO-1042-NW-1109</c>.</summary>
public sealed record RecordEvidence(string Id, string Label) : EvidenceItem(Id, Label);

/// <summary>A fact computed from records, holding the exact record ids it was computed from.</summary>
public sealed record DerivedEvidence(string Id, string Label, IReadOnlyList<string> SourceIds) : EvidenceItem(Id, Label);

/// <summary>How evidence ids are spelled. A derived id is built from its inputs, so the same fact gets the same id.</summary>
public static class EvidenceIds
{
    public const string FactPrefix = "fact:";

    public static string Merchant(string id) => $"merchant:{id}";

    public static string Hold(string id) => $"hold:{id}";

    public static string Processor(string id) => $"processor:{id}";

    public static string Payment(string id) => $"payment:{id}";

    public static string File(string id) => $"file:{id}";

    public static string Payout(string id) => $"payout:{id}";

    public static string Incident(string id) => $"incident:{id}";

    public static string Event(string id) => $"event:{id}";

    public static string ExpectedBatch(string paymentId) => Fact("expected-batch", paymentId);

    public static string MissingFile(string processorId, DateOnly batchDate) => Fact("missing-file", processorId, Iso(batchDate));

    public static string NonSettlementDay(DateOnly date) => Fact("non-settlement-day", Iso(date));

    public static string PaymentSets(string processorId, DateOnly batchDate) => Fact("payment-sets", processorId, Iso(batchDate));

    public static string Overdue(string payoutId) => Fact("overdue", payoutId);

    // The range is part of the key: the same group counted over different days is a different fact.
    public static string EventGroup(string processorId, string? merchantId, string category, string code, DateRange range) =>
        Fact("event-group", processorId, merchantId ?? "*", category, code, Iso(range.Start), Iso(range.End));

    public static bool IsDerived(string id) => id.StartsWith(FactPrefix, StringComparison.Ordinal);

    private static string Fact(string kind, params string[] key) => $"{FactPrefix}{kind}:{string.Join(':', key)}";

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
