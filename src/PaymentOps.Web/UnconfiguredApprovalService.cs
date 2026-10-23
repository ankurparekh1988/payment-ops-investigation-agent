using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web;

/// <summary>Development without Azure endpoints: no investigation runs, so there's never a proposal to decide.</summary>
internal sealed class UnconfiguredApprovalService : IApprovalService
{
    public Task<ApprovalResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalResult>(new ApprovalResult.NotFound());

    public Task<ApprovalResult> RejectAsync(Guid proposalId, RejectionReason reason, string? note, CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalResult>(new ApprovalResult.NotFound());
}
