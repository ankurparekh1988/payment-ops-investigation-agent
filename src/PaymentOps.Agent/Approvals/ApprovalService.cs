using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Security;

namespace PaymentOps.Agent.Approvals;

/// <summary>
/// The only way a ticket is created: the owning Engineer's decision on a stored proposal. Approval time is the
/// host's real clock; the data's investigation clock has no say in it.
/// </summary>
internal sealed class ApprovalService(ProposalStore proposals, IIncidentStore tickets, IUserContext user, ILogger<ApprovalService> logger)
    : IApprovalService
{
    private const int MaxNoteLength = 500;

    public async Task<ApprovalResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        // Signed in and an Engineer now, then the owner's pending, unexpired proposal, checked and claimed in one step.
        if (!MayDecide())
        {
            return new ApprovalResult.NotAuthorized();
        }

        if (!proposals.TryMove(proposalId, user.UserId, ProposalState.Pending, e => e with { State = ProposalState.Executing }, out var entry))
        {
            return Refusal(entry);
        }

        var proposal = entry!.Proposal;
        IncidentTicket ticket;
        try
        {
            // The proposal id is the idempotency key: a retry after a failure finds the ticket already made.
            ticket = await tickets.CreateAsync(proposalId.ToString(), Content(proposal), cancellationToken);
        }
        catch (Exception ex)
        {
            proposals.TryMove(proposalId, user.UserId, ProposalState.Executing, e => e with { State = ProposalState.Pending }, out _);
            ApprovalLog.StoreFailed(logger, proposal.InvestigationId, proposalId, ex);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new ApprovalResult.Unavailable();
        }

        proposals.TryMove(proposalId, user.UserId, ProposalState.Executing, e => e with { State = ProposalState.Executed, TicketId = ticket.Id }, out _);
        ApprovalLog.Decided(logger, proposal, "approved", null, ticket.Id, user.UserId);
        return new ApprovalResult.Created(ticket.Id);
    }

    public Task<ApprovalResult> RejectAsync(Guid proposalId, RejectionReason reason, string? note, CancellationToken cancellationToken)
    {
        if (!MayDecide())
        {
            return Task.FromResult<ApprovalResult>(new ApprovalResult.NotAuthorized());
        }

        // The note is the person's own text: it's never logged, and it's bounded before it's kept.
        var kept = note is { Length: > MaxNoteLength } ? note[..MaxNoteLength] : note;
        reason = Enum.IsDefined(reason) ? reason : RejectionReason.Other;
        if (!proposals.TryMove(proposalId, user.UserId, ProposalState.Pending, e => e with { State = ProposalState.Rejected, Reason = reason, Note = kept }, out var entry))
        {
            return Task.FromResult(Refusal(entry));
        }

        ApprovalLog.Decided(logger, entry!.Proposal, "rejected", reason, null, user.UserId);
        return Task.FromResult<ApprovalResult>(new ApprovalResult.Rejected());
    }

    private bool MayDecide() => user.IsAuthenticated && user.IsInRole(OpsRoles.Engineer);

    // A missing proposal and someone else's are the same answer. One still being created isn't decided yet.
    private static ApprovalResult Refusal(ProposalEntry? entry) => entry?.State switch
    {
        null => new ApprovalResult.NotFound(),
        ProposalState.Superseded => new ApprovalResult.Superseded(),
        ProposalState.Expired => new ApprovalResult.Expired(),
        ProposalState.Executed => new ApprovalResult.AlreadyDecided(entry.TicketId),
        ProposalState.Executing => new ApprovalResult.Unavailable(),
        _ => new ApprovalResult.AlreadyDecided(null),
    };

    private IncidentTicketContent Content(Proposal proposal)
    {
        var c = proposal.Content;
        return new IncidentTicketContent(
            c.ProcessorId, c.MerchantIds, c.StartDate, c.EndDate, c.Title, c.Summary,
            [.. c.Evidence.Select(e => e.Id)], c.ProcessorReference, proposal.InvestigationId, user.UserId);
    }
}
