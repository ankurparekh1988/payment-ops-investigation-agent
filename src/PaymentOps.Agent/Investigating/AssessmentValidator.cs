using System.Globalization;
using System.Text.RegularExpressions;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Decides whether a submission may be shown. Each failure names the rule and the field, never the model's
/// text, so it can go back to the model for one correction without echoing anything.
/// </summary>
internal static partial class AssessmentValidator
{
    private static readonly string[] CountWords =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
         "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];

    public static IReadOnlyList<string> Check(AssessmentSubmission submission, TurnState turn)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var failures = new List<string>();
        Supported("summary", submission.Summary, turn, failures);
        for (var i = 0; i < submission.Claims.Count; i++)
        {
            Supported($"claims[{i}]", submission.Claims[i], turn, failures);
        }

        Statement("root_cause", submission.RootCause, turn, failures, blankAllowed: true);
        for (var i = 0; i < submission.NextSteps.Count; i++)
        {
            Text($"next_steps[{i}]", submission.NextSteps[i].Text, failures);
            Citations($"next_steps[{i}]", submission.NextSteps[i].CitationIds, turn, failures);
        }

        switch (submission.Outcome)
        {
            case "assessed" when submission.Claims.Count == 0:
                failures.Add("claims: an assessed outcome needs at least one claim.");
                break;
            case "refused" when submission.Claims.Count > 0 || !string.IsNullOrWhiteSpace(submission.RootCause.Text):
                failures.Add("outcome: a refusal carries no claims and no root cause.");
                break;
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
            Text(field, statement.Text, failures);
        }

        foreach (var id in statement.EvidenceIds.Where(id => !turn.TryGetEvidence(id, out _)))
        {
            failures.Add($"{field}.evidence_ids: '{Shown(id)}' wasn't returned by a tool in this investigation.");
        }

        Citations(field, statement.CitationIds, turn, failures);
        CountsCiteTheirAggregate(field, statement, turn, failures);
    }

    private static void Text(string field, string text, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            failures.Add($"{field}.text: can't be blank.");
        }
        else if (IdToken().IsMatch(text))
        {
            failures.Add($"{field}.text: ids and citation markers belong in evidence_ids and citation_ids, not in the text.");
        }
    }

    private static void Citations(string field, IReadOnlyList<string> citationIds, TurnState turn, List<string> failures)
    {
        foreach (var id in citationIds.Where(id => !turn.TryGetSection(id, out _)))
        {
            failures.Add($"{field}.citation_ids: '{Shown(id)}' wasn't retrieved in this investigation.");
        }
    }

    // A count is supported by the aggregate that counted every event; the few event ids shown can't stand in for it.
    private static void CountsCiteTheirAggregate(string field, SubmittedStatement statement, TurnState turn, List<string> failures)
    {
        var cited = statement.EvidenceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var eventId in cited.Where(id => id.StartsWith("event:", StringComparison.Ordinal)))
        {
            foreach (var group in turn.EventGroupsContaining(eventId))
            {
                if (!cited.Contains(group.Id) && !group.SourceIds.All(cited.Contains) && MentionsCount(statement.Text, group.SourceIds.Count))
                {
                    failures.Add($"{field}.evidence_ids: a count of these events is supported by '{group.Id}'; cite it.");
                    return;
                }
            }
        }
    }

    private static bool MentionsCount(string text, int count) =>
        Regex.IsMatch(text, $@"\b{count.ToString(CultureInfo.InvariantCulture)}\b")
        || (count < CountWords.Length && Regex.IsMatch(text, $@"\b{CountWords[count]}\b", RegexOptions.IgnoreCase));

    // Failure text goes back to the model and into logs, so an id it invented is cut short.
    private static string Shown(string id) => id.Length <= 80 ? id : id[..80];

    [GeneratedRegex(@"\[S\d+\]|\b(merchant|hold|processor|payment|file|payout|incident|event|calendar|fact):[A-Za-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex IdToken();
}
