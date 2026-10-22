using System.Globalization;

using PaymentOps.Agent.Contracts;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Turns a valid submission into what people see. The label, the outcome and the confidence ceiling come from
/// what the server observed this turn; the model's own choices can only be lowered, never raised.
/// </summary>
internal static class AssessmentPolicy
{
    // Shown for every refusal in place of the model's words: a refusal is the server's notice, not a finding.
    public const string RefusalNotice =
        "This can't be investigated here. An investigation reads merchants' payments, settlement, payouts, processor status and error events, and the operations knowledge; it can't change anything.";

    public static AssessmentResult.Assessed Apply(AssessmentSubmission submission, TurnState turn)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var outcome = Enum.Parse<AssessmentOutcome>(submission.Outcome, ignoreCase: true);
        var gaps = turn.Gaps;

        // Asserts nothing about the data, so nothing the turn observed changes it.
        if (outcome == AssessmentOutcome.Refused)
        {
            return new AssessmentResult.Assessed(outcome, new Statement(RefusalNotice, [], []), [], null, Confidence.Low, [], [.. gaps.Select(g => g.Wording)]);
        }

        var confidence = Enum.Parse<Confidence>(submission.Confidence, ignoreCase: true);
        var rootCause = string.IsNullOrWhiteSpace(submission.RootCause.Text) ? null : new RootCause(Resolve(submission.RootCause, turn), Label(submission.RootCause));

        // High needs every statement people read to stand on both data and knowledge, each on its own.
        if (gaps.Count > 0
            || submission.Claims.Prepend(submission.Summary).Any(s => s.EvidenceIds.Count == 0 || s.CitationIds.Count == 0)
            || rootCause?.Label == RootCauseLabel.Hypothesis)
        {
            confidence = Min(confidence, Confidence.Medium);
        }

        if (turn.EveryStructuredSourceFailed)
        {
            outcome = AssessmentOutcome.Incomplete;
            rootCause = null;
        }
        else if (turn.Stopped)
        {
            outcome = AssessmentOutcome.Incomplete;
        }

        if (outcome == AssessmentOutcome.Incomplete)
        {
            confidence = Min(confidence, Confidence.Low);
        }

        // An incomplete investigation can't ground a ticket, whatever the model proposed.
        var ticket = submission.ProposedTicket;
        var withheld = ticket is not null && outcome == AssessmentOutcome.Incomplete;
        return new AssessmentResult.Assessed(
            outcome,
            Resolve(submission.Summary, turn),
            [.. submission.Claims.Select(c => Resolve(c, turn))],
            rootCause,
            confidence,
            [.. submission.NextSteps.Select(n => Resolve(new SubmittedStatement(n.Text, [], n.CitationIds), turn))],
            [.. gaps.Select(g => g.Wording)])
        {
            Proposal = ticket is null || withheld ? null : Draft(ticket, turn),
            ProposalWithheld = withheld,
        };
    }

    /// <summary>
    /// The processor's reference from the incidents the ticket cites for its processor, when they agree on one.
    /// Read from typed incident data only; model text is never searched for it.
    /// </summary>
    public static string? Reference(ProposedTicket ticket, TurnState turn)
    {
        var references = ticket.EvidenceIds
            .Select(id => turn.TryGetEvidence(id, out var item) ? item : null)
            .OfType<IncidentEvidence>()
            .Where(incident => incident.ProcessorId == ticket.ProcessorId && incident.ProcessorReference is not null)
            .Select(incident => incident.ProcessorReference!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return references.Count == 1 ? references[0] : null;
    }

    // Only called after validation, so the dates parse and every id resolves.
    private static TicketProposal Draft(ProposedTicket ticket, TurnState turn) => new(
        null,
        ProposalStatus.Draft,
        ticket.ProcessorId,
        [.. ticket.MerchantIds.Distinct(StringComparer.Ordinal)],
        DateOnly.ParseExact(ticket.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly.ParseExact(ticket.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        ticket.Title,
        ticket.Summary,
        [.. ticket.EvidenceIds.Distinct(StringComparer.Ordinal).Select(id => Evidence(id, turn))],
        [.. ticket.CitationIds.Distinct(StringComparer.Ordinal).Select(turn.CitationFor)],
        Reference(ticket, turn),
        null,
        null);

    // Data (a record or a fact computed from records) and knowledge together make an assessment.
    private static RootCauseLabel Label(SubmittedStatement rootCause) =>
        rootCause.EvidenceIds.Count > 0 && rootCause.CitationIds.Count > 0 ? RootCauseLabel.Assessment : RootCauseLabel.Hypothesis;

    private static Statement Resolve(SubmittedStatement statement, TurnState turn) => new(
        statement.Text,
        [.. statement.EvidenceIds.Distinct(StringComparer.Ordinal).Select(id => Evidence(id, turn))],
        [.. statement.CitationIds.Distinct(StringComparer.Ordinal).Select(turn.CitationFor)]);

    // Only called after validation, so every id resolves.
    private static EvidenceReference Evidence(string id, TurnState turn)
    {
        turn.TryGetEvidence(id, out var item);
        return new EvidenceReference(id, item.Label, item is DerivedEvidence);
    }

    private static Confidence Min(Confidence a, Confidence b) => a < b ? a : b;
}
