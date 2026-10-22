using System.Text.RegularExpressions;

using PaymentOps.Agent.Citations;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Decides whether a submission may be shown. Each failure names the rule and the field, never the model's
/// text, so it can go back to the model for one correction without echoing anything.
/// </summary>
internal static class AssessmentValidator
{
    // Built from the evidence id prefixes, so a new kind of evidence is checked without editing this.
    private static readonly Regex IdInText = new(
        $@"\b({string.Join('|', EvidenceIds.Prefixes.Select(Regex.Escape))})[A-Za-z0-9]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <param name="today">The investigation's today: a proposed ticket's dates are the data's.</param>
    public static IReadOnlyList<string> Check(AssessmentSubmission submission, TurnState turn, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var failures = new List<string>();

        // Checked here, not left to the wire schema: the server never parses a value it hasn't allowed.
        if (submission.Outcome is not ("assessed" or "incomplete" or "escalated" or "refused"))
        {
            failures.Add("outcome: must be assessed, incomplete, escalated or refused.");
        }

        if (submission.Confidence is not ("high" or "medium" or "low"))
        {
            failures.Add("confidence: must be high, medium or low.");
        }

        // A refusal's summary is never shown, so it asks for nothing; the server's notice stands in its place.
        if (submission.Outcome == "refused")
        {
            if (submission.Claims.Count > 0 || !string.IsNullOrWhiteSpace(submission.RootCause.Text) || submission.NextSteps.Count > 0)
            {
                failures.Add("outcome: a refusal carries no claims, no root cause and no next steps.");
            }

            if (submission.ProposedTicket is not null)
            {
                failures.Add("proposed_ticket: a refusal carries no ticket.");
            }

            return failures;
        }

        Supported("summary", submission.Summary, turn, failures);
        for (var i = 0; i < submission.Claims.Count; i++)
        {
            Supported($"claims[{i}]", submission.Claims[i], turn, failures);
        }

        Statement("root_cause", submission.RootCause, turn, failures, blankAllowed: true);
        for (var i = 0; i < submission.NextSteps.Count; i++)
        {
            Text($"next_steps[{i}].text", submission.NextSteps[i].Text, failures);
            Citations($"next_steps[{i}]", submission.NextSteps[i].CitationIds, turn, failures);
        }

        if (submission.Outcome == "assessed" && submission.Claims.Count == 0)
        {
            failures.Add("claims: an assessed outcome needs at least one claim.");
        }

        // An incomplete outcome's ticket is withheld whatever it says, so it isn't checked and can't cost the correction.
        if (submission.ProposedTicket is { } ticket && submission.Outcome != "incomplete")
        {
            ProposalValidator.Check(ticket, turn, today, failures);
        }

        return failures;
    }

    // The summary and every claim are factual text a person will read, so each needs support.
    private static void Supported(string field, SubmittedStatement statement, TurnState turn, List<string> failures)
    {
        if (statement.EvidenceIds.Count == 0 && statement.CitationIds.Count == 0)
        {
            failures.Add($"{field}: needs at least one evidence id or citation id.");
        }

        Statement(field, statement, turn, failures, blankAllowed: false);
    }

    private static void Statement(string field, SubmittedStatement statement, TurnState turn, List<string> failures, bool blankAllowed)
    {
        if (!(blankAllowed && string.IsNullOrWhiteSpace(statement.Text)))
        {
            Text($"{field}.text", statement.Text, failures);
        }

        Evidence(field, statement.EvidenceIds, turn, failures);
        Citations(field, statement.CitationIds, turn, failures);
        PartialGroupsCiteTheGroup(field, statement, turn, failures);
    }

    public static void Evidence(string field, IReadOnlyList<string> evidenceIds, TurnState turn, List<string> failures)
    {
        foreach (var id in evidenceIds.Where(id => !turn.TryGetEvidence(id, out _)))
        {
            failures.Add($"{field}.evidence_ids: '{Shown(id)}' wasn't returned by a tool in this investigation.");
        }
    }

    /// <param name="field">The field as the schema names it, such as <c>claims[0].text</c>.</param>
    public static void Text(string field, string text, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            failures.Add($"{field}: can't be blank.");
        }
        else if (IdInText.IsMatch(text) || CitationScanner.Scan(text) is { Citations.Count: > 0 } or { HasMalformedMarker: true })
        {
            failures.Add($"{field}: ids and citation markers belong in evidence_ids and citation_ids, not in the text.");
        }
    }

    public static void Citations(string field, IReadOnlyList<string> citationIds, TurnState turn, List<string> failures)
    {
        foreach (var id in citationIds.Where(id => !turn.TryGetSection(id, out _)))
        {
            failures.Add($"{field}.citation_ids: '{Shown(id)}' wasn't retrieved in this investigation.");
        }
    }

    // A count is supported by the group that counted every event; citing a few of its events can't stand in for it.
    private static void PartialGroupsCiteTheGroup(string field, SubmittedStatement statement, TurnState turn, List<string> failures)
    {
        // The same events counted over two ranges are two groups; citing or fully covering either is enough.
        var cited = statement.EvidenceIds.ToHashSet(StringComparer.Ordinal);
        var uncovered = cited.Where(EvidenceIds.IsEvent)
            .Select(turn.EventGroupsContaining)
            .FirstOrDefault(groups => groups.Count > 0 && !groups.Any(group => cited.Contains(group.Id) || group.SourceIds.All(cited.Contains)));
        if (uncovered is not null)
        {
            failures.Add($"{field}.evidence_ids: these events are part of '{uncovered[0].Id}'; cite it too.");
        }
    }

    // Failure text goes back to the model and into logs, so an id it invented is cut short.
    private static string Shown(string id) => id.Length <= 80 ? id : id[..80];
}
