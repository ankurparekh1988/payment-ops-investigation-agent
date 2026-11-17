using AngleSharp.Dom;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Security;
using PaymentOps.Web.Components.Pages;
using PaymentOps.Web.Investigating;

namespace PaymentOps.Web.SmokeTests.Investigating;

/// <summary>
/// Leaving the Investigate page and coming back, as Home ↔ Investigate does within one circuit: the page is disposed
/// and rendered again over the same scoped session.
/// </summary>
public sealed class InvestigatePageNavigationTests : InteractiveTestContext
{
    private readonly ScriptedInvestigationService _investigation = new();
    private readonly CountingApprovalService _approvals = new(new ApprovalResult.Created("INC-0042"));

    public InvestigatePageNavigationTests()
    {
        Context.Services.AddSingleton<IInvestigationService>(_investigation);
        Context.Services.AddSingleton<IApprovalService>(_approvals);
        Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Results.Now));
        Context.Services.AddSingleton<IUserContext>(TestUser.Engineer);
        Context.Services.AddSingleton(new QuestionLimits(2000));
        Context.Services.AddScoped<InvestigationSession>();
    }

    private InvestigationSession Session => Context.Services.GetRequiredService<InvestigationSession>();

    private static IElement Button(IRenderedComponent<Investigate> page, string text) =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static void Ask(IRenderedComponent<Investigate> page, string question)
    {
        page.Find("textarea#question").Input(question);
        Button(page, "Investigate").Click();
    }

    [Fact]
    public async Task The_result_survives_leaving_and_returning()
    {
        _investigation.Answer = _ => Results.WithPendingTicket("The Fabrikam Pay file for 9 November is missing.");
        var page = Render<Investigate>();
        Ask(page, "Why was M-1042 not paid?");
        page.WaitForAssertion(() => Assert.Contains("The Fabrikam Pay file for 9 November is missing.", page.Markup, StringComparison.Ordinal));

        await Context.DisposeComponentsAsync();
        var back = Render<Investigate>();

        Assert.Contains("The Fabrikam Pay file for 9 November is missing.", back.Markup, StringComparison.Ordinal);
        Assert.Contains("Why was M-1042 not paid?", back.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_continues_while_away_and_cancel_works_on_return()
    {
        _investigation.Hold = true;
        var page = Render<Investigate>();
        Ask(page, "Why was M-1042 not paid?");

        await Context.DisposeComponentsAsync();
        _investigation.LastProgress!.Report(new InvestigationProgress.ToolStarted("get_payout", "Payout P-1"));
        var back = Render<Investigate>();

        Assert.False(_investigation.LastToken.IsCancellationRequested, "leaving the page doesn't cancel the run");
        Assert.Contains("Payout P-1", back.Markup, StringComparison.Ordinal);
        Button(back, "Cancel").Click();
        back.WaitForAssertion(() => Assert.Equal(NotAssessedReason.Cancelled, Assert.IsType<AssessmentResult.NotAssessed>(Session.Current).Reason));
    }

    [Fact]
    public async Task A_pending_card_is_approved_after_returning()
    {
        _investigation.Answer = _ => Results.WithPendingTicket();
        var page = Render<Investigate>();
        Ask(page, "Why was M-1042 not paid?");
        page.WaitForAssertion(() => Assert.NotNull(Session.Current));

        await Context.DisposeComponentsAsync();
        var back = Render<Investigate>();
        Button(back, "Approve").Click();

        back.WaitForAssertion(() => Assert.Contains("Ticket INC-0042 created", back.Markup, StringComparison.Ordinal));
        Assert.Equal(1, _approvals.Approvals);
    }

    [Fact]
    public async Task An_expired_pending_card_shows_expired_on_return()
    {
        _investigation.Answer = _ => Results.WithPendingTicket();
        var page = Render<Investigate>();
        Ask(page, "Why was M-1042 not paid?");
        page.WaitForAssertion(() => Assert.NotNull(Session.Current));

        await Context.DisposeComponentsAsync();
        ((FakeTimeProvider)Context.Services.GetRequiredService<TimeProvider>()).Advance(TimeSpan.FromMinutes(16));
        var back = Render<Investigate>();

        Assert.Contains("This proposal expired before it was approved.", back.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(back.FindAll("button"), b => b.TextContent.Trim() == "Approve");
    }

    [Fact]
    public async Task A_failure_survives_leaving_and_returning_with_the_earlier_result_under_its_own_question()
    {
        _investigation.Answer = _ => Results.WithPendingTicket("The Fabrikam Pay file for 9 November is missing.");
        var page = Render<Investigate>();
        Ask(page, "Why was M-1042 not paid?");
        page.WaitForAssertion(() => Assert.NotNull(Session.Current));
        _investigation.Throw = new InvalidOperationException("boom");
        Ask(page, "Why is M-2001 held?");
        page.WaitForAssertion(() => Assert.NotNull(Session.Notice));

        await Context.DisposeComponentsAsync();
        var back = Render<Investigate>();

        Assert.Contains("The investigation service isn't responding right now.", back.Markup, StringComparison.Ordinal);
        Assert.Equal("Why was M-1042 not paid?", back.Find(".result-question").TextContent);
        Assert.Contains("The Fabrikam Pay file for 9 November is missing.", back.Markup, StringComparison.Ordinal);
        Assert.Contains("This proposal can't be approved right now.", back.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(back.FindAll("button"), b => b.TextContent.Trim() == "Approve");
        Assert.Equal(2, _investigation.Asked.Count);
    }

    [Fact]
    public void A_draft_from_home_is_filled_not_run()
    {
        Session.Draft = "Why is M-1042's payout held?";

        var page = Render<Investigate>();

        Assert.Equal("Why is M-1042's payout held?", page.Find("textarea#question").GetAttribute("value") ?? page.Find("textarea#question").TextContent);
        Assert.Empty(_investigation.Asked);
    }

    [Fact]
    public void New_investigation_clears_the_page()
    {
        _investigation.Answer = _ => Results.WithPendingTicket("A summary to clear.");
        var page = Render<Investigate>();
        Ask(page, "Why was M-1042 not paid?");
        page.WaitForAssertion(() => Assert.Contains("A summary to clear.", page.Markup, StringComparison.Ordinal));

        Button(page, "New investigation").Click();

        Assert.DoesNotContain("A summary to clear.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(1, _investigation.StartNewCalls);
    }

    // A notice and an older refusal both offer Try again; each retries the question it's about.
    [Fact]
    public void Each_panel_retries_its_own_question()
    {
        _investigation.Answer = q => q == "First question"
            ? new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable) { InvestigationId = Guid.NewGuid() }
            : new AssessmentResult.NotAssessed(NotAssessedReason.Busy);
        var page = Render<Investigate>();
        Ask(page, "First question");
        page.WaitForAssertion(() => Assert.NotNull(Session.Current));
        Ask(page, "Second question");
        page.WaitForAssertion(() => Assert.NotNull(Session.Notice));

        var retries = page.FindAll("button").Where(b => b.TextContent.Trim() == "Try again").ToList();
        Assert.Equal(2, retries.Count);
        retries[1].Click();

        Assert.Equal("First question", _investigation.Asked[^1]);
    }
}
