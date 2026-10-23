using System.Globalization;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Components.Pages;

/// <summary>Fixed text for each reason. No model or provider wording ever reaches the page.</summary>
internal static class NotAssessedText
{
    public static string For(NotAssessedReason reason, int maxQuestionLength) => reason switch
    {
        NotAssessedReason.InvalidQuestion => string.Create(CultureInfo.InvariantCulture, $"Please enter a question of up to {maxQuestionLength:N0} characters."),
        NotAssessedReason.NotAuthorized => "Please sign in again to investigate.",
        NotAssessedReason.Busy => "Another investigation is already running.",
        NotAssessedReason.ModelUnavailable => "The investigation service isn't responding right now. Please try again shortly.",
        NotAssessedReason.LimitReached => "The investigation stopped at its limit before reaching an assessment. Please try a narrower question.",
        NotAssessedReason.AssessmentUnverified => "I couldn't produce an assessment I can back with evidence. Please try rephrasing the question.",
        NotAssessedReason.Cancelled => "Investigation cancelled. Nothing from it was kept.",
        NotAssessedReason.ConversationLimit => "This conversation is full. Start a new investigation to continue.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
