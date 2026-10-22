using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Security;

namespace PaymentOps.Agent.Approvals;

/// <summary>
/// The only way a ticket is created: the owning Engineer's decision on a stored proposal. Approval time is the
/// host's real clock; the data's investigation clock has no say in it.
/// </summary>
internal sealed class ApprovalService(ProposalStore proposals, IIncidentStore tickets, IUserContext user, TimeProvider time, ILogger<ApprovalService> logger)
    : IApprovalService
{
    private const int MaxNoteLength = 500;

    public async Task<ApprovalResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        if (Decidable(proposalId) is not { } proposal)
        {
            return Refusal(proposalId);
        }

        if (!proposals.TryBeginExecute(proposalId))
        {
            return Refusal(proposalId);
        }

        IncidentTicket ticket;
        try
        {
            // The proposal id is the idempotency key: a retry after a failure finds the ticket already made.
            ticket = await tickets.CreateAsync(proposalId.ToString(), Content(proposal), cancellationToken);
        }
        catch (Exception ex)
        {
            proposals.Revert(proposalId);
            ApprovalLog.StoreFailed(logger, proposal.InvestigationId, proposalId, ex);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new ApprovalResult.Unavailable();
        }

        proposals.Complete(proposalId, ticket.Id);
        ApprovalLog.Decided(logger, proposal, "approved", null, ticket.Id, user.UserId);
        return new ApprovalResult.Created(ticket.Id);
    }

    public Task<ApprovalResult> RejectAsync(Guid proposalId, RejectionReason reason, string? note, CancellationToken cancellationToken)
    {
        // The note is the person's own text: it's never logged, and it's bounded before it's kept.
        var kept = note is { Length: > MaxNoteLength } ? note[..MaxNoteLength] : note;
        if (!Enum.IsDefined(reason))
        {
            reason = RejectionReason.Other;
        }

        if (Decidable(proposalId) is not { } proposal || !proposals.TryReject(proposalId, reason, kept))
        {
            return Task.FromResult(Refusal(proposalId));
        }

        ApprovalLog.Decided(logger, proposal, "rejected", reason, null, user.UserId);
        return Task.FromResult<ApprovalResult>(new ApprovalResult.Rejected());
    }

    // Checked in this order on every call, so nothing about a proposal leaks to someone who can't decide it.
    private Proposal? Decidable(Guid proposalId)
    {
        if (!user.IsAuthenticated || !user.IsInRole(OpsRoles.Engineer))
        {
            return null;
        }

        return proposals.Find(proposalId) is { State: ProposalState.Pending } entry
            && entry.Proposal.OwnerId == user.UserId
            && entry.Proposal.ExpiresAt > time.GetUtcNow()
            ? entry.Proposal
            : null;
    }

    private ApprovalResult Refusal(Guid proposalId)
    {
        if (!user.IsAuthenticated || !user.IsInRole(OpsRoles.Engineer))
        {
            return new ApprovalResult.NotAuthorized();
        }

        if (proposals.Find(proposalId) is not { } entry || entry.Proposal.OwnerId != user.UserId)
        {
            return new ApprovalResult.NotFound();
        }

        return entry.State switch
        {
            ProposalState.Superseded => new ApprovalResult.Superseded(),
            ProposalState.Executed => new ApprovalResult.AlreadyDecided(entry.TicketId),
            ProposalState.Rejected or ProposalState.Executing => new ApprovalResult.AlreadyDecided(null),
            _ => new ApprovalResult.Expired(),
        };
    }

    private IncidentTicketContent Content(Proposal proposal)
    {
        var c = proposal.Content;
        return new IncidentTicketContent(
            c.ProcessorId, c.MerchantIds, c.StartDate, c.EndDate, c.Title, c.Summary,
            [.. c.Evidence.Select(e => e.Id)], c.ProcessorReference, proposal.InvestigationId, user.UserId);
    }
}
