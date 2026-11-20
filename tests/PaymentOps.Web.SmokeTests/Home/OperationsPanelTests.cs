using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using MudBlazor;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Components.Home;
using PaymentOps.Web.Components.Layout;
using PaymentOps.Web.Home;
using PaymentOps.Web.Security;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Home;

/// <summary>The Admin's pause control asks before acting and shows what the server answered.</summary>
public sealed class OperationsPanelTests : InteractiveTestContext
{
    private readonly InMemoryPauseStore _store = new();
    private readonly TestRecentAuthentication _recent = new();
    private CountingPauseSwitch? _switch;

    public OperationsPanelTests()
    {
        var admin = TestUser.Engineer with { Roles = OpsRoles.Effective([OpsRoles.Admin]) };
        Context.Services.AddSingleton<IPauseStore>(_store);
        Context.Services.AddSingleton<IUserContext>(admin);
        Context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Context.Services.AddSingleton<IRecentAuthentication>(_recent);
        Context.Services.AddSingleton<IPauseSwitch>(sp => _switch = new CountingPauseSwitch(
            new PauseSwitch(_store, admin, _recent, TimeProvider.System, NullLogger<PauseSwitch>.Instance)));
    }

    private static OperationsView View(bool pausedByConfiguration = false, PauseState? pause = null) =>
        new(3, 150, 25, pausedByConfiguration, pause ?? PauseState.Running, null, null);

    [Fact]
    public void Pausing_asks_first_and_shows_the_new_state()
    {
        var dialogs = Render<MudDialogProvider>();
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View()));

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Pause investigations").Click();
        dialogs.WaitForAssertion(() => Assert.Contains("New investigations stop within 10 seconds. Investigations already running will finish.", dialogs.Markup, StringComparison.Ordinal));
        Assert.False(_store.Current.Paused);

        dialogs.Find("textarea").Input("Processor maintenance");
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Pause investigations").Click();

        panel.WaitForAssertion(() => Assert.Contains("Paused", panel.Find(".pause-status").TextContent, StringComparison.Ordinal));
        Assert.True(_store.Current.Paused);
        Assert.Equal("Processor maintenance", _store.Current.Reason);
        Assert.Contains("Processor maintenance", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_running_changes_nothing()
    {
        var dialogs = Render<MudDialogProvider>();
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View()));

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Pause investigations").Click();
        dialogs.WaitForAssertion(() => Assert.Contains(dialogs.FindAll("button"), b => b.TextContent.Trim() == "Keep running"));
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Keep running").Click();

        Assert.False(_store.Current.Paused);
    }

    [Fact]
    public void Configuration_pause_wins_and_resume_is_disabled()
    {
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View(pausedByConfiguration: true)));

        Assert.Contains("Paused by configuration", panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(panel.FindAll("button"), b => b.TextContent.Trim() == "Pause investigations");
        var resume = panel.FindAll("button").Single(b => b.TextContent.Trim() == "Resume investigations");
        Assert.True(resume.HasAttribute("disabled"));
    }

    [Fact]
    public void The_last_change_says_who_when_and_why()
    {
        var pause = new PauseState(true, "u-1", "Ada Admin", new DateTimeOffset(2026, 11, 16, 9, 5, 0, TimeSpan.Zero), "Processor maintenance");

        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View(pause: pause)));

        Assert.Contains("Ada Admin", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("16 Nov 09:05 UTC", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("Processor maintenance", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_pause_says_so_and_offers_no_switch()
    {
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, new OperationsView(3, 150, 25, false, null, null, null)));

        Assert.Contains("can't be read", panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(panel.FindAll("button"), b => b.TextContent.Contains("investigations", StringComparison.Ordinal));
    }

    // MudBlazor's dialog gives focus back to whatever had it before; the panel puts it on the button that opened it.
    [Fact]
    public void Closing_the_dialog_returns_focus_to_the_pause_button()
    {
        var dialogs = Render<MudDialogProvider>();
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View()));
        var focusesBefore = Context.JSInterop.Invocations.Count(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal));

        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Pause investigations").Click();
        dialogs.WaitForAssertion(() => Assert.Contains(dialogs.FindAll("button"), b => b.TextContent.Trim() == "Keep running"));
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Keep running").Click();

        panel.WaitForAssertion(() => Assert.True(
            Context.JSInterop.Invocations.Count(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal)) > focusesBefore,
            "focus is moved back after the dialog closes"));
    }

    // Development without the usage guard has no limits; a zero would read as "nobody may investigate".
    [Fact]
    public void Unset_limits_say_so_rather_than_zero()
    {
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, new OperationsView(null, 0, 0, false, PauseState.Running, null, null)));

        Assert.Contains("Not set", panel.Find(".ops-per-person").TextContent, StringComparison.Ordinal);
    }

    private static void AskToPause(IRenderedComponent<MudDialogProvider> dialogs, IRenderedComponent<OperationsPanel> panel)
    {
        panel.FindAll("button").Single(b => b.TextContent.Trim() == "Pause investigations").Click();
        dialogs.WaitForAssertion(() => Assert.Contains(dialogs.FindAll("button"), b => b.TextContent.Trim() == "Pause investigations"));
        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Pause investigations").Click();
    }

    [Fact]
    public void Stale_pause_shows_confirm()
    {
        _recent.Recent = false;
        var dialogs = Render<MudDialogProvider>();
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View()));

        AskToPause(dialogs, panel);

        panel.WaitForAssertion(() => Assert.Contains("For this you need to have signed in within the last 15 minutes.", panel.Markup, StringComparison.Ordinal));
        Assert.Equal("Confirm it's you", panel.Find("button[data-po-confirm]").TextContent.Trim());
        Assert.False(_store.Current.Paused);
    }

    [Fact]
    public async Task Confirmed_does_not_perform_the_action()
    {
        _recent.Recent = false;
        var dialogs = Render<MudDialogProvider>();
        var panel = Render<OperationsPanel>(p => p.Add(o => o.View, View()));
        AskToPause(dialogs, panel);
        panel.WaitForAssertion(() => panel.Find("button[data-po-confirm]"));
        var calls = _switch!.Calls;

        _recent.Recent = true;
        var confirm = panel.FindComponent<ConfirmIdentity>();
        var flow = panel.Find("button[data-po-confirm]").GetAttribute("data-po-confirm")!;
        await confirm.InvokeAsync(() => confirm.Instance.OnConfirmResult(flow, "confirmed"));

        panel.WaitForAssertion(() => Assert.Contains("Confirmed. Choose Pause again.", panel.Markup, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll("button[data-po-confirm]"));
        Assert.Equal(calls, _switch.Calls);
        Assert.False(_store.Current.Paused);
        Assert.DoesNotContain("New investigations stop within 10 seconds.", dialogs.Markup, StringComparison.Ordinal);
    }

    private sealed class CountingPauseSwitch(IPauseSwitch inner) : IPauseSwitch
    {
        public int Calls { get; private set; }

        public Task<PauseResult> SetAsync(bool paused, string? reason, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.SetAsync(paused, reason, cancellationToken);
        }
    }
}
