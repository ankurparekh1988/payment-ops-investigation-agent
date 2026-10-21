using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Turns a valid submission into what people see. The label, the outcome and the confidence ceiling come from
/// what the server observed this turn; the model's own choices can only be lowered, never raised.
/// </summary>
internal static class AssessmentPolicy
{
    public static AssessmentResult.Assessed Apply(AssessmentSubmission submission, TurnState turn)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var outcome = Enum.Parse<AssessmentOutcome>(submission.Outcome, ignoreCase: true);
        var confidence = Enum.Parse<Confidence>(submission.Confidence, ignoreCase: true);
        var rootCause = string.IsNullOrWhiteSpace(submission.RootCause.Text) ? null : new RootCause(Resolve(submission.RootCause, turn), Label(submission.RootCause));

        var gaps = turn.Gaps;
        var statements = submission.Claims.Prepend(submission.Summary).ToList();
        if (gaps.Count > 0 || statements.All(s => s.CitationIds.Count == 0) || statements.All(s => s.EvidenceIds.Count == 0))
        {
            confidence = Min(confidence, Confidence.Medium);
        }

        if (turn.EveryStructuredSourceFailed)
        {
            outcome = AssessmentOutcome.Incomplete;
            rootCause = null;
        }
        else if (turn.Stopped && outcome != AssessmentOutcome.Refused)
        {
            outcome = AssessmentOutcome.Incomplete;
        }

        if (outcome == AssessmentOutcome.Incomplete)
        {
            confidence = Min(confidence, Confidence.Low);
        }

        return new AssessmentResult.Assessed(
            outcome,
            Resolve(submission.Summary, turn),
            [.. submission.Claims.Select(c => Resolve(c, turn))],
            rootCause,
            confidence,
            [.. submission.NextSteps.Select(n => Resolve(new SubmittedStatement(n.Text, [], n.CitationIds), turn))],
            [.. gaps.Select(g => g.Wording)]);
    }

    // Data (a record or a fact computed from records) and knowledge together make an assessment.
    private static RootCauseLabel Label(SubmittedStatement rootCause) =>
        rootCause.EvidenceIds.Count > 0 && rootCause.CitationIds.Count > 0 ? RootCauseLabel.Assessment : RootCauseLabel.Hypothesis;

    private static Statement Resolve(SubmittedStatement statement, TurnState turn) => new(
        statement.Text,
        [.. statement.EvidenceIds.Distinct(StringComparer.Ordinal).Select(id => Evidence(id, turn))],
        [.. statement.CitationIds.Distinct(StringComparer.Ordinal).Select(id => Citation(id, turn))]);

    // Only called after validation, so every id resolves.
    private static EvidenceReference Evidence(string id, TurnState turn)
    {
        turn.TryGetEvidence(id, out var item);
        return new EvidenceReference(id, item.Label, item is DerivedEvidence);
    }

    private static Citation Citation(string id, TurnState turn)
    {
        turn.TryGetSection(id, out var section);
        return EvidenceRegistry.Cite(id, section);
    }

    private static Confidence Min(Confidence a, Confidence b) => a < b ? a : b;
}
