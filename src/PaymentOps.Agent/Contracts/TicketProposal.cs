namespace PaymentOps.Agent.Contracts;

public enum ProposalStatus
{
    /// <summary>Shown only: the person can't approve it, so nothing is kept on the server.</summary>
    Draft,
    Pending,

    /// <summary>The server couldn't keep the proposal, so it can't be approved.</summary>
    Unavailable,
    Approved,
    Rejected,
    Expired,
    Superseded,
}

/// <summary>
/// An incident ticket the agent proposed, as the server validated it. Evidence and citations are the server's
/// descriptions; <paramref name="ProcessorReference"/> comes from typed incident data, never from model text.
/// </summary>
public sealed record TicketProposal(
    Guid? ProposalId,
    ProposalStatus Status,
    string ProcessorId,
    IReadOnlyList<string> MerchantIds,
    DateOnly StartDate,
    DateOnly EndDate,
    string Title,
    string Summary,
    IReadOnlyList<EvidenceReference> Evidence,
    IReadOnlyList<Citation> Citations,
    string? ProcessorReference,
    DateTimeOffset? ExpiresAt,
    string? TicketId)
{
    /// <summary>Why the owner rejected it, once they have.</summary>
    public RejectionReason? Rejection { get; init; }
}
