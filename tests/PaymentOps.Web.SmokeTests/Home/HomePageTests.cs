using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;
using PaymentOps.Domain.Settlement;
using PaymentOps.Web.Components.Pages;
using PaymentOps.Web.Home;
using PaymentOps.Web.Investigating;
using PaymentOps.Web.SmokeTests.Investigating;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Home;

/// <summary>The rendered home page shows each role exactly the sections its overview fetched.</summary>
public sealed class HomePageTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    private async Task<string> HomeFor(string name, string role, params string[] groups)
    {
        using var client = factory.CreateClientFor(name, [role], groups);
        return await client.GetStringAsync(new Uri("/", UriKind.Relative));
    }

    [Fact]
    public async Task A_reader_sees_the_shared_sections_only()
    {
        var html = await HomeFor("Riley Reader", OpsRoles.Reader);

        Assert.Contains("Processor incidents", html, StringComparison.Ordinal);
        Assert.Contains("Settlements needing attention", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting for your approval", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent tickets", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations", html.Replace("Payment Operations", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_engineer_also_sees_approvals_and_tickets()
    {
        var html = await HomeFor("Eli Engineer", OpsRoles.Engineer);

        Assert.Contains("Waiting for your approval", html, StringComparison.Ordinal);
        Assert.Contains("Recent tickets", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations", html.Replace("Payment Operations", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_admin_also_sees_operations()
    {
        var html = await HomeFor("Ada Admin", OpsRoles.Admin);

        Assert.Contains("Recent tickets", html, StringComparison.Ordinal);
        Assert.Contains(">Operations<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Risk_and_compliance_adds_the_restricted_knowledge_badge()
    {
        var member = await HomeFor("Rita Risk", OpsRoles.Reader, PaymentOpsWebApplicationFactory.RestrictedGroupId);
        var other = await HomeFor("Riley Reader", OpsRoles.Reader);

        Assert.Contains("Restricted knowledge", member, StringComparison.Ordinal);
        Assert.DoesNotContain("Restricted knowledge", other, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shipped_demo_data_fills_the_shared_sections()
    {
        var html = await HomeFor("Riley Reader", OpsRoles.Reader);

        Assert.Contains("Fabrikam Pay", html, StringComparison.Ordinal);
        Assert.Contains("M-2087 payout held", html, StringComparison.Ordinal);
        Assert.Contains("Fabrikam Pay file rejected", html, StringComparison.Ordinal);
        Assert.Contains("Data as of", html, StringComparison.Ordinal);
    }
}

/// <summary>Home hands a question to Investigate through the session; it never runs one.</summary>
public sealed class HomeInteractionTests : InteractiveTestContext
{
    private readonly ScriptedInvestigationService _investigation = new();
    private readonly OverviewPorts _ports = new() { Now = new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero) };

    public HomeInteractionTests()
    {
        Context.Services.AddSingleton<IInvestigationService>(_investigation);
        Context.Services.AddSingleton<IApprovalService>(new CountingApprovalService(new ApprovalResult.Rejected()));
        Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Results.Now));
        Context.Services.AddSingleton<IUserContext>(TestUser.Engineer);
        Context.Services.AddSingleton(new QuestionLimits(2000));
        Context.Services.AddSingleton<IProcessorData>(_ports);
        Context.Services.AddSingleton<ISettlementData>(_ports);
        Context.Services.AddSingleton<IIncidentStore>(_ports);
        Context.Services.AddSingleton<IInvestigationClock>(_ports);
        Context.Services.AddSingleton<IUsageCounter>(new InMemoryUsageCounter());
        Context.Services.Configure<UsageOptions>(o => (o.DailyLimitPerUser, o.DailyLimitTotal) = (25, 150));
        Context.Services.AddSingleton(Options.Create(new UiOptions()));
        Context.Services.AddScoped<InvestigationSession>();
        Context.Services.AddScoped<HomeOverview>();
    }

    private InvestigationSession Session => Context.Services.GetRequiredService<InvestigationSession>();

    [Fact]
    public void Investigate_from_a_row_fills_the_question_and_runs_nothing()
    {
        var home = Render<PaymentOps.Web.Components.Pages.Home>();
        home.WaitForAssertion(() => Assert.NotEmpty(home.FindAll(".attention-row button")));

        home.FindAll(".attention-row button")[0].Click();

        Assert.Equal("Why is M-2087's payout for 4 Nov held?", Session.Draft);
        Assert.EndsWith("/investigate", Context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Empty(_investigation.Asked);
    }

    [Fact]
    public void A_starter_question_fills_the_box_and_continue_hands_it_over()
    {
        var home = Render<PaymentOps.Web.Components.Pages.Home>();

        home.FindAll(".starter-question")[0].Click();
        home.FindAll("button").Single(b => b.TextContent.Trim() == "Continue in Investigate").Click();

        Assert.Equal(StarterQuestions.All[0], Session.Draft);
        Assert.EndsWith("/investigate", Context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Empty(_investigation.Asked);
    }

    [Fact]
    public async Task A_running_investigation_is_shown_with_a_link_back()
    {
        _investigation.Hold = true;
        var run = Session.StartAsync("Why was M-1042 not paid?");

        var home = Render<PaymentOps.Web.Components.Pages.Home>();

        Assert.Contains("Investigation running", home.Markup, StringComparison.Ordinal);
        Session.Cancel();
        await run;
    }

    [Fact]
    public async Task A_pending_ticket_from_this_session_is_waiting_for_approval()
    {
        _investigation.Answer = _ => Results.WithPendingTicket();
        await Session.StartAsync("Why was M-1042 not paid?");

        var home = Render<PaymentOps.Web.Components.Pages.Home>();

        home.WaitForAssertion(() => Assert.Contains("Fabrikam Pay settlement file missing", home.Markup, StringComparison.Ordinal));
        Assert.Contains("Review", home.Markup, StringComparison.Ordinal);
    }
}
