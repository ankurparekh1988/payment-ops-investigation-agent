namespace PaymentOps.Agent.Contracts;

/// <summary>Why no answer was given. Distinct for diagnosis; the UI groups them into a few messages.</summary>
public enum NotAnsweredReason
{
    InvalidQuestion,
    NotAuthorized,
    Busy,
    KnowledgeUnavailable,
    NoEvidence,
    AnswerUnavailable,
    AnswerBlocked,
    AnswerIncomplete,
    AnswerUnverifiable,
    InsufficientEvidence,
}
