using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Security;
using PaymentOps.Web.Home;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Home;

/// <summary>
/// The home page's data, built by role: each role gets everything the one below it gets plus its own sections, and a
/// section a role doesn't get is never fetched. Demo data is read as of the demo clock, usage as of today.
/// </summary>
public sealed class HomeOverviewTests
{
    private static readonly DateTimeOffset WallNow = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private readonly OverviewPorts _ports = new();
    private readonly InMemoryUsageCounter _counter = new();
    private readonly InMemoryPauseStore _pause = new();

    private HomeOverview Overview(IUserContext user, UsageOptions? usage = null, UiOptions? ui = null, bool withCounter = true) => new(
        user, _ports, _ports, _ports, _ports, new FakeTimeProvider(WallNow),
        new StaticOptionsMonitor<UsageOptions>(usage ?? new UsageOptions { DailyLimitPerUser = 25, DailyLimitTotal = 150 }),
        Options.Create(ui ?? new UiOptions()),
        _pause,
        withCounter ? _counter : null);

    private static TestUser Admin => TestUser.Engineer with { Roles = OpsRoles.Effective([OpsRoles.Admin]) };

    [Fact]
    public async Task A_reader_gets_no_engineer_or_admin_sections()
    {
        var view = await Overview(TestUser.Reader).LoadAsync(CancellationToken.None);

        Assert.False(view.ShowApprovals);
        Assert.Null(view.Tickets);
        Assert.Null(view.Operations);
        Assert.Equal(0, _ports.TicketReads);
        Assert.NotEmpty(view.Processors);
        Assert.NotEmpty(view.Attention);
        Assert.NotNull(view.Usage);
    }

    [Fact]
    public async Task An_engineer_gets_approvals_and_tickets_not_operations()
    {
        _ports.Tickets.Add(Ticket("INC-0001", WallNow.AddMinutes(-5)));

        var view = await Overview(TestUser.Engineer).LoadAsync(CancellationToken.None);

        Assert.True(view.ShowApprovals);
        Assert.Equal(["INC-0001"], view.Tickets!.Select(t => t.Id));
        Assert.Null(view.Operations);
    }

    [Fact]
    public async Task An_admin_gets_everything()
    {
        var ui = new UiOptions { WorkbookUrl = new Uri("https://portal.azure.com/#workbook"), AlertsUrl = new Uri("https://portal.azure.com/#alerts") };
        await _counter.TryReserveAsync(TestUser.Engineer.UserId, DateOnly.FromDateTime(WallNow.UtcDateTime), 25, 150, CancellationToken.None);
        await _counter.TryReserveAsync("someone-else", DateOnly.FromDateTime(WallNow.UtcDateTime), 25, 150, CancellationToken.None);

        var view = await Overview(Admin, ui: ui).LoadAsync(CancellationToken.None);

        Assert.True(view.ShowApprovals);
        Assert.NotNull(view.Tickets);
        var operations = Assert.IsType<OperationsView>(view.Operations);
        Assert.Equal(2, operations.TotalToday);
        Assert.Equal(150, operations.TotalLimit);
        Assert.Equal(25, operations.PerUserLimit);
        Assert.Equal(ui.WorkbookUrl, operations.WorkbookUrl);
        Assert.Equal(ui.AlertsUrl, operations.AlertsUrl);
        Assert.Equal(PauseState.Running, operations.Pause);
        Assert.Equal(new UsageToday(1, 25), view.Usage);
    }

    // A circuit whose sign-in ended, or someone without a role, gets nothing read on their behalf.
    [Fact]
    public async Task Someone_without_a_role_gets_nothing_fetched()
    {
        var view = await Overview(TestUser.Anonymous).LoadAsync(CancellationToken.None);

        Assert.Empty(view.Processors);
        Assert.Empty(view.Attention);
        Assert.Null(view.Usage);
        Assert.Null(view.Tickets);
        Assert.Null(view.Operations);
        Assert.Empty(_ports.IncidentWindows);
        Assert.Empty(_ports.AttentionWindows);
    }

    [Fact]
    public async Task Recent_tickets_are_the_newest_five()
    {
        for (var i = 1; i <= 7; i++)
        {
            _ports.Tickets.Add(Ticket($"INC-{i:D4}", WallNow.AddMinutes(i)));
        }

        var view = await Overview(TestUser.Engineer).LoadAsync(CancellationToken.None);

        Assert.Equal(["INC-0007", "INC-0006", "INC-0005", "INC-0004", "INC-0003"], view.Tickets!.Select(t => t.Id));
    }

    [Fact]
    public async Task Attention_items_use_the_demo_clock()
    {
        var view = await Overview(TestUser.Reader).LoadAsync(CancellationToken.None);

        // The demo clock reads 11 Nov, so batch dates 4 to 11 Nov count; the wall clock (16 Nov) plays no part.
        Assert.Equal((new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 12)), _ports.AttentionWindows.Single());
        Assert.Equal((_ports.Now.AddDays(-7), _ports.Now), _ports.IncidentWindows.Single());
        Assert.Equal(_ports.Now, view.AsOf);
        Assert.Equal(["PO-1 4 Nov", "SF-FAB-1104-X 4 Nov"], view.Attention.Select(a => $"{(a.Kind == AttentionKind.HeldPayout ? "PO-1" : "SF-FAB-1104-X")} {a.BatchDate:d MMM}"));
    }

    [Fact]
    public async Task Attention_items_are_newest_first_with_a_fixed_question()
    {
        _ports.Now = new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);

        var view = await Overview(TestUser.Reader).LoadAsync(CancellationToken.None);

        Assert.Equal(
            [
                "Why is M-2087's payout for 4 Nov held?",
                "Why was fabrikam-pay's settlement file for 4 Nov rejected?",
                "Why is M-2087's payout for 2 Nov held?",
            ],
            view.Attention.Select(a => a.Question));
        Assert.Equal("Northwind Acquiring", view.Attention[0].ProcessorName);
    }

    [Fact]
    public async Task Processor_incidents_carry_the_processor_name()
    {
        var view = await Overview(TestUser.Reader).LoadAsync(CancellationToken.None);

        var incident = Assert.Single(view.Processors);
        Assert.Equal("Fabrikam Pay", incident.ProcessorName);
        Assert.Equal("Settlement batch delayed", incident.Summary);
        Assert.Null(incident.ResolvedAt);
    }

    [Fact]
    public async Task Usage_reads_the_counter_without_reserving()
    {
        var today = DateOnly.FromDateTime(WallNow.UtcDateTime);
        await _counter.TryReserveAsync(TestUser.Reader.UserId, today, 25, 150, CancellationToken.None);

        var first = await Overview(TestUser.Reader).LoadAsync(CancellationToken.None);
        var second = await Overview(TestUser.Reader).LoadAsync(CancellationToken.None);

        Assert.Equal(new UsageToday(1, 25), first.Usage);
        Assert.Equal(new UsageToday(1, 25), second.Usage);
    }

    [Fact]
    public async Task Usage_that_cant_be_read_is_reported_not_guessed()
    {
        var view = await Overview(Admin, withCounter: false).LoadAsync(CancellationToken.None);

        Assert.Null(view.Usage);
        Assert.Null(view.Operations!.TotalToday);
    }

    [Fact]
    public async Task A_pause_in_configuration_shows_on_the_operations_section()
    {
        var view = await Overview(Admin, usage: new UsageOptions { Paused = true, DailyLimitPerUser = 25, DailyLimitTotal = 150 }).LoadAsync(CancellationToken.None);

        Assert.True(view.Operations!.PausedByConfiguration);
    }

    [Fact]
    public void Starter_questions_are_fixed()
    {
        Assert.Equal(3, StarterQuestions.All.Count);
        Assert.Contains("Why were payouts for merchant M-1042 delayed yesterday?", StarterQuestions.All);
    }

    private static IncidentTicket Ticket(string id, DateTimeOffset createdAt) => new(
        id,
        new IncidentTicketContent("fabrikam-pay", ["M-1042"], new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9), $"Ticket {id}", "Summary",
            [], null, Guid.NewGuid(), "00000000-0000-0000-0000-000000000002"),
        createdAt);

}
