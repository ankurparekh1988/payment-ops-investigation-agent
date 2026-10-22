using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Investigation;

namespace PaymentOps.Web.SmokeTests;

public sealed class TicketCardTests
{
    private static readonly Guid Id = Guid.Parse("0f0e0d0c-0000-4000-8000-000000000013");

    private static TicketProposal Proposal(ProposalStatus status, string title = "Fabrikam Pay settlement file missing for 9 November") => new(
        status == ProposalStatus.Draft || status == ProposalStatus.Unavailable ? null : Id,
        status,
        "fabrikam-pay",
        ["M-1042"],
        new DateOnly(2026, 11, 9),
        new DateOnly(2026, 11, 9),
        title,
        "No settlement file arrived from Fabrikam Pay for the 9 November batch.",
        [new EvidenceReference("fact:missing-file:fabrikam-pay:2026-11-09", "No Fabrikam Pay file for 2026-11-09", true)],
        [new Citation("S1", "payout-delay--abc", "Payout delay", "Payout delay > The batch didn't close")],
        "FAB-7731",
        status == ProposalStatus.Pending ? new DateTimeOffset(2026, 10, 22, 14, 32, 0, TimeSpan.Zero) : null,
        status == ProposalStatus.Approved ? "INC-0001" : null);

    private static Task<string> Render(TicketProposal proposal) =>
        ComponentHtml.RenderAsync<TicketCard>(new() { [nameof(TicketCard.Proposal)] = proposal });

    [Fact]
    public async Task A_draft_shows_the_reader_wording_and_no_buttons()
    {
        var html = await Render(Proposal(ProposalStatus.Draft));

        Assert.Contains("Draft prepared. An Operations Engineer must run their own investigation before a ticket can be created.", html, StringComparison.Ordinal);
        Assert.Contains("Fabrikam Pay settlement file missing for 9 November", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pending_proposal_shows_approve_reject_and_expiry()
    {
        var html = await Render(Proposal(ProposalStatus.Pending));

        Assert.Contains(">Approve<", html, StringComparison.Ordinal);
        Assert.Contains(">Reject<", html, StringComparison.Ordinal);
        Assert.Contains("14:32 UTC", html, StringComparison.Ordinal);
        Assert.Contains("FAB-7731", html, StringComparison.Ordinal);
        Assert.Contains("M-1042", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/sources/payout-delay--abc\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decided_proposal_is_read_only()
    {
        var approved = await Render(Proposal(ProposalStatus.Approved));
        var rejected = await Render(Proposal(ProposalStatus.Rejected) with { Rejection = RejectionReason.NotAnIncident });

        Assert.Contains("Ticket INC-0001 created", approved, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", approved, StringComparison.Ordinal);
        Assert.Contains("Rejected: not an incident", rejected, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", rejected, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProposalStatus.Unavailable, "can't be approved right now")]
    [InlineData(ProposalStatus.Expired, "expired")]
    [InlineData(ProposalStatus.Superseded, "replaced by a later proposal")]
    public async Task A_proposal_that_cannot_be_decided_says_why(ProposalStatus status, string words)
    {
        var html = await Render(Proposal(status));

        Assert.Contains(words, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Model_text_is_escaped()
    {
        var html = await Render(Proposal(ProposalStatus.Pending, title: "<script>x</script><b>bold</b>"));

        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_withheld_proposal_is_explained()
    {
        var withheld = new AssessmentResult.Assessed(AssessmentOutcome.Incomplete, new Statement("Partial.", [], []), [], null, Confidence.Low, [], [])
        {
            ProposalWithheld = true,
        };

        var html = await ComponentHtml.RenderAsync<AssessmentView>(new() { [nameof(AssessmentView.Result)] = withheld });

        Assert.Contains("A ticket was proposed but isn't shown, because the investigation is incomplete.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assessment_shows_its_proposal()
    {
        var assessed = new AssessmentResult.Assessed(AssessmentOutcome.Assessed, new Statement("Missing file.", [], []), [], null, Confidence.Medium, [], [])
        {
            Proposal = Proposal(ProposalStatus.Draft),
        };

        var html = await ComponentHtml.RenderAsync<AssessmentView>(new() { [nameof(AssessmentView.Result)] = assessed });

        Assert.Contains("Draft prepared.", html, StringComparison.Ordinal);
    }
}
