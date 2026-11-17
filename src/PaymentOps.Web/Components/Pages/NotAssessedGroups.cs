using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Components.Pages;

/// <summary>What the person can do about a result that isn't an assessment.</summary>
public enum NotAssessedGroup
{
    YouCanFixIt,
    TryAgainShortly,
    LimitOrPolicy,
    Cancelled,
    SignedOut,
    Support,
}

public static class NotAssessedGroups
{
    public static NotAssessedGroup For(NotAssessedReason reason) => reason switch
    {
        NotAssessedReason.InvalidQuestion or NotAssessedReason.LimitReached or NotAssessedReason.AssessmentUnverified
            or NotAssessedReason.ConversationLimit => NotAssessedGroup.YouCanFixIt,
        NotAssessedReason.ModelUnavailable or NotAssessedReason.Unscreened or NotAssessedReason.UsageUnavailable
            or NotAssessedReason.Busy => NotAssessedGroup.TryAgainShortly,
        NotAssessedReason.Blocked or NotAssessedReason.Paused or NotAssessedReason.DailyLimitReached => NotAssessedGroup.LimitOrPolicy,
        NotAssessedReason.Cancelled => NotAssessedGroup.Cancelled,
        NotAssessedReason.NotAuthorized => NotAssessedGroup.SignedOut,
        NotAssessedReason.BlockedSelfHarm => NotAssessedGroup.Support,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
