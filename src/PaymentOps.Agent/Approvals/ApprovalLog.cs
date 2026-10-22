using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Approvals;

/// <summary>
/// One <c>approval.decided</c> event per decision, ids and codes only: never the title, summary, note, question or
/// any model text. The approver is a pseudonym, enough to tell decisions by the same person apart.
/// </summary>
internal static partial class ApprovalLog
{
    public static void Decided(ILogger logger, Proposal proposal, string decision, RejectionReason? reason, string? ticketId, string approverId)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var approver = Pseudonym(approverId);
            Decided(logger, proposal.InvestigationId, proposal.Id, decision, reason, ticketId, approver, proposal.Model);
        }
    }

    // Only the exception's type: a store's message could echo the ticket's content.
    public static void StoreFailed(ILogger logger, Guid investigationId, Guid proposalId, Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            StoreFailed(logger, investigationId, proposalId, exception.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 3101, EventName = "approval.decided", Level = LogLevel.Information,
        Message = "Approval decided: investigation {InvestigationId}, proposal {ProposalId}, {Decision}, reason {ReasonCode}, ticket {TicketId}, approver {ApproverId}, release {Release}")]
    private static partial void Decided(ILogger logger, Guid investigationId, Guid proposalId, string decision, RejectionReason? reasonCode, string? ticketId, string approverId, string release);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning, Message = "Ticket creation failed: investigation {InvestigationId}, proposal {ProposalId}, {ExceptionType}; the proposal is pending again")]
    private static partial void StoreFailed(ILogger logger, Guid investigationId, Guid proposalId, string exceptionType);

    private static string Pseudonym(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..16];
}
