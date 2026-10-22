namespace PaymentOps.Domain.Incidents;

/// <summary>
/// What an approved ticket says. <paramref name="StartDate"/> and <paramref name="EndDate"/> are inclusive settlement dates;
/// <paramref name="CreatedBy"/> is the approver's user id.
/// </summary>
public sealed record IncidentTicketContent(
    string ProcessorId,
    IReadOnlyList<string> MerchantIds,
    DateOnly StartDate,
    DateOnly EndDate,
    string Title,
    string Summary,
    IReadOnlyList<string> EvidenceIds,
    string? ProcessorReference,
    Guid InvestigationId,
    string CreatedBy);

public sealed record IncidentTicket(string Id, IncidentTicketContent Content, DateTimeOffset CreatedAt);
