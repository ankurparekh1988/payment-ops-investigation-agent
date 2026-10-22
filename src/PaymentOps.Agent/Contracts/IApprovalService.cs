namespace PaymentOps.Agent.Contracts;

/// <summary>
/// Decides a pending ticket proposal. Every call re-checks, at that moment, that the caller is signed in, is an
/// Operations Engineer, owns the proposal, and that it's still pending and unexpired. There's no HTTP route to it.
/// </summary>
public interface IApprovalService
{
    /// <summary>Creates the ticket from the stored proposal, once, whatever the number of calls.</summary>
    Task<ApprovalResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken);

    /// <param name="note">Up to 500 characters of plain text, kept with the decision and never logged.</param>
    Task<ApprovalResult> RejectAsync(Guid proposalId, RejectionReason reason, string? note, CancellationToken cancellationToken);
}

public enum RejectionReason
{
    EvidenceDoesNotSupportIt,
    WrongMerchantsOrWindow,
    NotAnIncident,
    AlreadyRaised,
    Other,
}

/// <summary>What a decision did. A missing proposal and someone else's are the same <see cref="NotFound"/>.</summary>
public abstract record ApprovalResult
{
    private ApprovalResult()
    {
    }

    public sealed record Created(string TicketId) : ApprovalResult;

    public sealed record Rejected : ApprovalResult;

    /// <param name="TicketId">The ticket, when the decision was to create one.</param>
    public sealed record AlreadyDecided(string? TicketId) : ApprovalResult;

    public sealed record NotAuthorized : ApprovalResult;

    public sealed record NotFound : ApprovalResult;

    public sealed record Expired : ApprovalResult;

    public sealed record Superseded : ApprovalResult;

    /// <summary>The ticket store failed or is full; the proposal is still pending.</summary>
    public sealed record Unavailable : ApprovalResult;
}
