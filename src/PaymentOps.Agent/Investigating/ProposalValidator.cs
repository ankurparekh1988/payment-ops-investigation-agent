using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// A proposed ticket must rest on this investigation's own records and knowledge, name only processors and
/// merchants a tool returned, and carry nothing that looks like card or account data.
/// </summary>
internal static class ProposalValidator
{
    private const string Field = "proposed_ticket";
    private const int MaxTitleLength = 120;
    private const int MaxSummaryLength = 1000;

    /// <param name="today">The investigation's today, so a ticket's window is judged by the data's dates.</param>
    public static void Check(ProposedTicket ticket, TurnState turn, DateOnly today, List<string> failures)
    {
        // A failure on two processors at once is the platform's, and one processor's ticket would misstate it.
        if (turn.ProcessorsWithMissingFiles.Count > 1)
        {
            failures.Add($"{Field}: missing files from more than one processor point to a platform incident; escalate without a ticket.");
        }

        if (!turn.TryGetEvidence(EvidenceIds.Processor(ticket.ProcessorId), out _))
        {
            failures.Add($"{Field}.processor_id: no processor record in this investigation.");
        }

        if (ticket.MerchantIds.Count == 0)
        {
            failures.Add($"{Field}.merchant_ids: needs at least one merchant.");
        }

        for (var i = 0; i < ticket.MerchantIds.Count; i++)
        {
            if (!turn.TryGetEvidence(EvidenceIds.Merchant(ticket.MerchantIds[i]), out _))
            {
                failures.Add($"{Field}.merchant_ids[{i}]: no merchant record in this investigation.");
            }
        }

        if (!ToolArguments.TryRange(ticket.StartDate, ticket.EndDate, today, out _, out _, out var rangeError))
        {
            failures.Add($"{Field}: {rangeError}");
        }

        Content($"{Field}.title", ticket.Title, MaxTitleLength, "120", failures);
        Content($"{Field}.summary", ticket.Summary, MaxSummaryLength, "1,000", failures);

        if (ticket.EvidenceIds.Count == 0)
        {
            failures.Add($"{Field}.evidence_ids: needs at least one record or computed fact.");
        }

        AssessmentValidator.Evidence(Field, ticket.EvidenceIds, turn, failures);

        if (ticket.CitationIds.Count == 0)
        {
            failures.Add($"{Field}.citation_ids: needs at least one knowledge citation.");
        }

        AssessmentValidator.Citations(Field, ticket.CitationIds, turn, failures);
    }

    private static void Content(string field, string text, int maxLength, string shownLimit, List<string> failures)
    {
        AssessmentValidator.Text(field, text, failures);
        if (text.Length > maxLength)
        {
            failures.Add($"{field}: can be at most {shownLimit} characters.");
        }

        // The rule id only: the matched value goes nowhere, not even back to the model.
        foreach (var rule in TicketContentPolicy.Violations(text))
        {
            failures.Add($"{field}: contains a {rule}.");
        }
    }
}
