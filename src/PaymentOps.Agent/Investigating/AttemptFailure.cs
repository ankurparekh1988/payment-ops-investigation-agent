using PaymentOps.Agent.Model;

namespace PaymentOps.Agent.Investigating;

/// <summary>Why an attempt ended without a validated result, typed where the cause is known (ADR 0023).</summary>
internal enum AttemptFailure
{
    /// <summary>The validator rejected the submission again after its one correction.</summary>
    RejectedAfterCorrection,

    /// <summary>No accepted submission after the required submission request.</summary>
    NoSubmission,

    /// <summary>The model called a tool that doesn't exist.</summary>
    UnknownToolCall,

    /// <summary>The tool budget, a call or token allowance, or the deadline stopped the attempt.</summary>
    LimitReached,

    ServiceFailed,

    Blocked,

    Aborted,

    Cancelled,
}

/// <summary>One attempt of an investigation: the tier it ran on and why it failed, or null for a validated result.</summary>
internal sealed record AttemptRecord(ModelTier Tier, AttemptFailure? Failure);

internal static class Escalation
{
    /// <summary>
    /// Only a capability failure escalates: the model couldn't produce a submission the server accepts. Policy, safety,
    /// authorization, outages, limits and cancellation never do, and nor does any validated result.
    /// </summary>
    public static bool IsEligible(AttemptFailure? failure) =>
        failure is AttemptFailure.RejectedAfterCorrection or AttemptFailure.NoSubmission;
}
