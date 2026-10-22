using PaymentOps.Agent.Investigating;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Tests;

public sealed class ProposalValidationTests
{
    private const string MissingFile = "fact:missing-file:fabrikam-pay:2026-11-09";
    private static readonly DateOnly Today = new(2026, 11, 11);

    private readonly TurnState _turn = Turn();

    private static TurnState Turn(bool secondProcessorMissing = false)
    {
        var turn = new TurnState();
        turn.Register([
            new RecordEvidence("merchant:M-1042", "Merchant M-1042"),
            new RecordEvidence("merchant:M-2087", "Merchant M-2087"),
            new RecordEvidence("processor:fabrikam-pay", "Fabrikam Pay"),
            new RecordEvidence("processor:northwind-acquiring", "Northwind Acquiring"),
            new DerivedEvidence(MissingFile, "No Fabrikam Pay file for 2026-11-09", ["processor:fabrikam-pay", "payment:PAY-1"]),
        ]);
        if (secondProcessorMissing)
        {
            turn.Register([new DerivedEvidence("fact:missing-file:northwind-acquiring:2026-11-09", "No Northwind file", ["processor:northwind-acquiring"])]);
        }

        turn.RegisterSection(new RetrievedSection("k1", "d", "Payout delay", "Payout delay > The batch didn't close", "text", "General", null));
        turn.StructuredOutcome(answered: true);
        return turn;
    }

    private static ProposedTicket Ticket() => new(
        "fabrikam-pay", ["M-1042"], "2026-11-09", "2026-11-09", "Fabrikam Pay settlement file missing for 9 November",
        "No settlement file arrived from Fabrikam Pay for the 9 November batch.", [MissingFile], ["S1"]);

    private static AssessmentSubmission With(ProposedTicket? ticket, string outcome = "assessed") => new(
        outcome,
        new SubmittedStatement("The 9 November file never arrived.", [MissingFile], ["S1"]),
        outcome == "refused" ? [] : [new SubmittedStatement("The runbook says to raise it.", [MissingFile], ["S1"])],
        new SubmittedStatement(string.Empty, [], []),
        "medium",
        [],
        ticket);

    private IReadOnlyList<string> Check(ProposedTicket ticket, TurnState? turn = null) => AssessmentValidator.Check(With(ticket), turn ?? _turn, Today);

    [Fact]
    public void A_grounded_ticket_is_valid() => Assert.Empty(Check(Ticket()));

    [Fact]
    public void A_null_ticket_needs_nothing() => Assert.Empty(AssessmentValidator.Check(With(null), _turn, Today));

    [Fact]
    public void A_refusal_carries_no_ticket() =>
        Assert.Contains("proposed_ticket: a refusal carries no ticket.", AssessmentValidator.Check(With(Ticket(), "refused"), _turn, Today));

    [Fact]
    public void A_ticket_needs_record_evidence() =>
        Assert.Contains("proposed_ticket.evidence_ids: needs at least one record or computed fact.", Check(Ticket() with { EvidenceIds = [] }));

    [Fact]
    public void A_ticket_needs_a_knowledge_citation() =>
        Assert.Contains("proposed_ticket.citation_ids: needs at least one knowledge citation.", Check(Ticket() with { CitationIds = [] }));

    [Fact]
    public void Unregistered_ids_are_rejected_by_field()
    {
        var failures = Check(Ticket() with { EvidenceIds = [MissingFile, "payout:PO-404"], CitationIds = ["S1", "S9"] });

        Assert.Contains("proposed_ticket.evidence_ids: 'payout:PO-404' wasn't returned by a tool in this investigation.", failures);
        Assert.Contains("proposed_ticket.citation_ids: 'S9' wasn't retrieved in this investigation.", failures);
    }

    [Fact]
    public void The_processor_must_be_a_record_in_this_investigation() =>
        Assert.Contains("proposed_ticket.processor_id: no processor record in this investigation.", Check(Ticket() with { ProcessorId = "contoso-pay" }));

    [Fact]
    public void Every_merchant_must_be_a_record_in_this_investigation() =>
        Assert.Contains("proposed_ticket.merchant_ids[1]: no merchant record in this investigation.", Check(Ticket() with { MerchantIds = ["M-1042", "M-9999"] }));

    [Fact]
    public void A_ticket_names_at_least_one_merchant() =>
        Assert.Contains("proposed_ticket.merchant_ids: needs at least one merchant.", Check(Ticket() with { MerchantIds = [] }));

    [Fact]
    public void Lists_are_capped()
    {
        var failures = Check(Ticket() with
        {
            MerchantIds = [.. Enumerable.Repeat("M-1042", 21)],
            EvidenceIds = [.. Enumerable.Repeat(MissingFile, 21)],
            CitationIds = [.. Enumerable.Repeat("S1", 11)],
        });

        Assert.Contains("proposed_ticket.merchant_ids: can list at most 20.", failures);
        Assert.Contains("proposed_ticket.evidence_ids: can list at most 20.", failures);
        Assert.Contains("proposed_ticket.citation_ids: can list at most 10.", failures);
    }

    [Theory]
    [InlineData("2026-11-09", "2026-11-12", "proposed_ticket: end_date can't be later than 2026-11-11.")]
    [InlineData("2026-11-10", "2026-11-09", "proposed_ticket: start_date can't be later than end_date.")]
    [InlineData("2026-10-01", "2026-11-09", "proposed_ticket: The range can be at most 31 days.")]
    [InlineData("9 Nov", "2026-11-09", "proposed_ticket: start_date and end_date must be dates written as YYYY-MM-DD.")]
    public void The_window_is_checked(string start, string end, string failure) =>
        Assert.Equal([failure], Check(Ticket() with { StartDate = start, EndDate = end }));

    [Fact]
    public void Title_and_summary_have_limits()
    {
        Assert.Contains("proposed_ticket.title: can be at most 120 characters.", Check(Ticket() with { Title = new string('t', 121) }));
        Assert.Contains("proposed_ticket.summary: can be at most 1,000 characters.", Check(Ticket() with { Summary = new string('s', 1001) }));
        Assert.Contains("proposed_ticket.title.text: can't be blank.", Check(Ticket() with { Title = " " }));
        Assert.Contains("proposed_ticket.summary.text: ids and citation markers belong in evidence_ids and citation_ids, not in the text.", Check(Ticket() with { Summary = "See [S1] and merchant:M-1042." }));
    }

    [Fact]
    public void A_card_number_is_named_by_rule_never_echoed()
    {
        var failures = Check(Ticket() with { Summary = "Customer card 4111 1111 1111 1111 was declined." });

        Assert.Contains("proposed_ticket.summary: contains a card_number.", failures);
        Assert.DoesNotContain(string.Concat(failures), char.IsDigit);
    }

    [Fact]
    public void Missing_files_from_two_processors_mean_escalation_without_a_ticket() =>
        Assert.Contains(
            "proposed_ticket: missing files from more than one processor point to a platform incident; escalate without a ticket.",
            Check(Ticket(), Turn(secondProcessorMissing: true)));

    // Real time is earlier than the data's as-of date; a ticket's window is the data's, so the data's today applies.
    [Fact]
    public void Dates_are_checked_against_the_investigation_clock()
    {
        Assert.Empty(AssessmentValidator.Check(With(Ticket()), _turn, new DateOnly(2026, 11, 11)));
        Assert.Contains("proposed_ticket: end_date can't be later than 2026-10-22.", AssessmentValidator.Check(With(Ticket()), _turn, new DateOnly(2026, 10, 22)));
    }

    [Fact]
    public void The_turn_knows_which_processors_have_missing_files()
    {
        Assert.Equal(["fabrikam-pay"], _turn.ProcessorsWithMissingFiles);
        Assert.Equal(2, Turn(secondProcessorMissing: true).ProcessorsWithMissingFiles.Count);
    }
}
