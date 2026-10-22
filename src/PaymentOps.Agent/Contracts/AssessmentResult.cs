namespace PaymentOps.Agent.Contracts;

public enum AssessmentOutcome
{
    Assessed,
    Incomplete,
    Escalated,
    Refused,
}

/// <summary>Categorical, never a probability: model-stated probabilities aren't calibrated.</summary>
public enum Confidence
{
    Low,
    Medium,
    High,
}

/// <summary>Set by the server: a root cause is an assessment only with both data and knowledge behind it.</summary>
public enum RootCauseLabel
{
    Assessment,
    Hypothesis,
}

/// <summary>Why there's no assessment. Distinct for diagnosis; the UI groups them into a few messages.</summary>
public enum NotAssessedReason
{
    InvalidQuestion,
    NotAuthorized,
    Busy,
    ModelUnavailable,
    LimitReached,
    AssessmentUnverified,

    /// <summary>The person stopped the investigation; nothing from it is kept.</summary>
    Cancelled,

    /// <summary>The conversation is full; a new investigation starts a fresh one.</summary>
    ConversationLimit,
}

/// <summary>A record or computed fact the server registered this turn, described from its own data.</summary>
public sealed record EvidenceReference(string Id, string Label, bool IsDerived);

/// <summary>A sentence and exactly what supports it, resolved from the server's evidence, never from model output.</summary>
public sealed record Statement(string Text, IReadOnlyList<EvidenceReference> Evidence, IReadOnlyList<Citation> Citations);

public sealed record RootCause(Statement Statement, RootCauseLabel Label);

/// <summary>A validated assessment, or the reason there isn't one.</summary>
public abstract record AssessmentResult
{
    private AssessmentResult()
    {
    }

    /// <summary>Ties the result to its logs and anything created from it.</summary>
    public Guid InvestigationId { get; init; }

    /// <param name="Gaps">What the investigation couldn't check, as the server observed it.</param>
    public sealed record Assessed(
        AssessmentOutcome Outcome,
        Statement Summary,
        IReadOnlyList<Statement> Claims,
        RootCause? RootCause,
        Confidence Confidence,
        IReadOnlyList<Statement> NextSteps,
        IReadOnlyList<string> Gaps) : AssessmentResult;

    public sealed record NotAssessed(NotAssessedReason Reason) : AssessmentResult;
}
