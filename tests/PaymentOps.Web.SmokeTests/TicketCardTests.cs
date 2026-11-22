using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Security;
using PaymentOps.Web.Components.Investigation;
using PaymentOps.Web.Components.Layout;
using PaymentOps.Web.Components.Pages;
using PaymentOps.Web.Investigating;
using PaymentOps.Web.Security;
using PaymentOps.Web.SmokeTests.Investigating;

namespace PaymentOps.Web.SmokeTests;

public sealed class TicketCardTests : InteractiveTestContext
{
    private static readonly Guid Id = Guid.Parse("0f0e0d0c-0000-4000-8000-000000000013");

    private readonly ScriptedInvestigationService _investigation = new() { Answer = _ => Results.WithPendingTicket() };
    private readonly CountingApprovalService _approvals = new(new ApprovalResult.Created("INC-0042"));
    private readonly TestRecentAuthentication _recent = new();

    public TicketCardTests()
    {
        Context.Services.AddSingleton<IInvestigationService>(_investigation);
        Context.Services.AddSingleton<IApprovalService>(_approvals);
        Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Results.Now));
        Context.Services.AddSingleton<IUserContext>(TestUser.Engineer);
        Context.Services.AddSingleton(new QuestionLimits(2000));
        Context.Services.AddSingleton<IRecentAuthentication>(_recent);
        Context.Services.AddScoped<InvestigationSession>();
    }

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
    [InlineData(ProposalStatus.CapacityReached, "demo ticket store has reached capacity")]
    [InlineData(ProposalStatus.Expired, "expired")]
    [InlineData(ProposalStatus.Superseded, "replaced by a later proposal")]
    public async Task A_proposal_that_cannot_be_decided_says_why(ProposalStatus status, string words)
    {
        var html = await Render(Proposal(status));

        Assert.Contains(words, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Every_status_has_a_stated_line(ProposalStatus status)
    {
        var html = await Render(Proposal(status));

        Assert.Contains("class=\"ticket-status", html, StringComparison.Ordinal);
        Assert.Equal(status == ProposalStatus.Pending, html.Contains("<button", StringComparison.Ordinal));
    }

    public static TheoryData<ProposalStatus> Statuses() => [.. Enum.GetValues<ProposalStatus>()];

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

    private static IElement Button(IRenderedComponent<Investigate> page, string text) =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private IRenderedComponent<Investigate> PendingCard()
    {
        var page = Render<Investigate>();
        page.Find("textarea#question").Input("Why was M-1042 not paid?");
        Button(page, "Investigate").Click();
        page.WaitForAssertion(() => Button(page, "Approve"));
        return page;
    }

    [Fact]
    public void Stale_approve_shows_confirm()
    {
        _recent.Recent = false;
        var page = PendingCard();

        Button(page, "Approve").Click();

        page.WaitForAssertion(() => Assert.Contains("For this you need to have signed in within the last 15 minutes.", page.Markup, StringComparison.Ordinal));
        Assert.Equal("Confirm it's you", page.Find("button[data-po-confirm]").TextContent.Trim());
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Trim() == "Approve");
        Assert.Equal(0, _approvals.Approvals);
    }

    [Fact]
    public async Task Confirmed_does_not_perform_the_action()
    {
        _recent.Recent = false;
        var page = PendingCard();
        Button(page, "Approve").Click();
        page.WaitForAssertion(() => page.Find("button[data-po-confirm]"));

        _recent.Recent = true;
        var confirm = page.FindComponent<ConfirmIdentity>();
        var flow = page.Find("button[data-po-confirm]").GetAttribute("data-po-confirm")!;
        await confirm.InvokeAsync(() => confirm.Instance.OnConfirmResult(flow, "confirmed"));

        page.WaitForAssertion(() => Button(page, "Approve"));
        Assert.Empty(page.FindAll("button[data-po-confirm]"));
        Assert.Equal(0, _approvals.Approvals);
        Assert.Contains("Awaiting approval", page.Markup, StringComparison.Ordinal);
    }
}
