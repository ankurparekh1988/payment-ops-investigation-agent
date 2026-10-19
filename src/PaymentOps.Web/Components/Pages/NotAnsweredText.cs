using System.Globalization;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Components.Pages;

/// <summary>Fixed text for each reason. No model or provider wording ever reaches the page.</summary>
internal static class NotAnsweredText
{
    public static string For(NotAnsweredReason reason, int maxQuestionLength) => reason switch
    {
        NotAnsweredReason.NoEvidence or NotAnsweredReason.InsufficientEvidence => "I couldn't find this in the documents you can access.",
        NotAnsweredReason.Busy => "Another question is already being processed.",
        NotAnsweredReason.InvalidQuestion => string.Create(CultureInfo.InvariantCulture, $"Please enter a question of up to {maxQuestionLength:N0} characters."),
        NotAnsweredReason.NotAuthorized => "Please sign in again to ask a question.",
        NotAnsweredReason.KnowledgeUnavailable or NotAnsweredReason.AnswerUnavailable => "The knowledge service isn't responding right now. Please try again shortly.",
        NotAnsweredReason.AnswerBlocked => "This question can't be answered.",
        NotAnsweredReason.AnswerIncomplete or NotAnsweredReason.AnswerUnverifiable => "I couldn't produce an answer I can back with sources. Please try rephrasing the question.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
