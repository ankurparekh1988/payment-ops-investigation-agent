using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Approvals;

/// <summary>
/// A proposal as the server keeps it: immutable, and the only thing an approval acts on. Its state lives in
/// <see cref="ProposalStore"/>, so nothing the page holds can change what gets created.
/// </summary>
/// <param name="Content">The validated draft, exactly as shown to the owner.</param>
/// <param name="Model">The prompt version and model that wrote it, for the record.</param>
internal sealed record Proposal(
    Guid Id,
    Guid InvestigationId,
    Guid ConversationId,
    string OwnerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Model,
    TicketProposal Content);

internal enum ProposalState
{
    Pending,
    Executing,
    Executed,
    Rejected,
    Expired,
    Superseded,

    /// <summary>The ticket store was full when it was approved. Terminal: the demo store never frees a ticket.</summary>
    CapacityReached,
}

/// <summary>A proposal and where it stands now. <paramref name="Note"/> is the rejecter's own text, never logged.</summary>
internal sealed record ProposalEntry(Proposal Proposal, ProposalState State, string? TicketId, RejectionReason? Reason, string? Note);
