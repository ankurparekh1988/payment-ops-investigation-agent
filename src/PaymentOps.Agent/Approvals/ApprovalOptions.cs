namespace PaymentOps.Agent.Approvals;

public sealed class ApprovalOptions
{
    public const string SectionName = "PaymentOps:Approvals";

    /// <summary>How long a proposal can be approved, on the host's real clock.</summary>
    public TimeSpan ProposalLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Pending proposals across all users; one more is shown as unavailable.</summary>
    public int MaxPendingProposals { get; set; } = 500;
}
