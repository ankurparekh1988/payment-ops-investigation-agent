using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using MudBlazor;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Components.Home;
using PaymentOps.Web.Home;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Home;

/// <summary>The Admin's pause control asks before acting and shows what the server answered.</summary>
public sealed class OperationsPanelTests : InteractiveTestContext
{
    private readonly InMemoryPauseStore _store = new();

    public OperationsPanelTests()
    {
        var admin = TestUser.Engineer with { Roles = OpsRoles.Effective([OpsRoles.Admin]) };
        Context.Services.AddSingleton<IPauseStore>(_store);
        Context.Services.AddSingleton<IUserContext>(admin);
        Context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Context.Services.AddSingleton<IPauseSwitch>(sp => new PauseSwitch(_store, admin, new TestRecentAuthentication(), TimeProvider.System, NullLogger<PauseSwitch>.Instance));
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
}
