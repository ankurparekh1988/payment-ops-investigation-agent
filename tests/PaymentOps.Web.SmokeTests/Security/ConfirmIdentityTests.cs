using System.Text.RegularExpressions;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Web.Components.Layout;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

/// <summary>
/// The .NET half of confirming in a popup: the browser opens it and reports back, and the component trusts only the
/// server's own record of a recent sign-in.
/// </summary>
public sealed partial class ConfirmIdentityTests : InteractiveTestContext
{
    private const string NotConfirmed = "Not confirmed. Try again.";

    private readonly TestRecentAuthentication _recent = new();
    private int _confirmed;

    public ConfirmIdentityTests() => Context.Services.AddSingleton<IRecentAuthentication>(_recent);

    private IRenderedComponent<ConfirmIdentity> RenderConfirm() =>
        Render<ConfirmIdentity>(p => p
            .Add(c => c.Action, "approve the ticket")
            .Add(c => c.Confirmed, () => _confirmed++));

    private static string Flow(IRenderedComponent<ConfirmIdentity> confirm) =>
        confirm.Find("button[data-po-confirm]").GetAttribute("data-po-confirm")!;

    private static Task Report(IRenderedComponent<ConfirmIdentity> confirm, string flow, string result) =>
        confirm.InvokeAsync(() => confirm.Instance.OnConfirmResult(flow, result));

    private IReadOnlyList<NavigationHistory> Navigations() =>
        [.. ((BunitNavigationManager)Context.Services.GetRequiredService<NavigationManager>()).History];

    [Fact]
    public void It_explains_why_and_offers_confirm()
    {
        var confirm = RenderConfirm();

        Assert.Contains("For this you need to have signed in within the last 15 minutes.", confirm.Markup, StringComparison.Ordinal);
        Assert.Equal("Confirm it's you", confirm.Find("button[data-po-confirm]").TextContent.Trim());
        confirm.WaitForAssertion(() => Assert.Contains(Context.JSInterop.Invocations, i => i.Identifier == "poSession.register" && Equals(i.Arguments[0], Flow(confirm))));
    }

    [Fact]
    public async Task Closing_the_popup_without_a_result_re_enables_confirm()
    {
        var confirm = RenderConfirm();
        var flow = Flow(confirm);

        await Report(confirm, flow, "closed");

        var button = confirm.Find("button[data-po-confirm]");
        Assert.False(button.HasAttribute("disabled"));
        Assert.NotEqual(flow, Flow(confirm));
        Assert.Contains(NotConfirmed, confirm.Markup, StringComparison.Ordinal);
        Assert.Equal(0, _confirmed);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("not-confirmed")]
    [InlineData("anything-else")]
    public async Task An_unconfirmed_result_asks_to_try_again(string result)
    {
        var confirm = RenderConfirm();

        await Report(confirm, Flow(confirm), result);

        Assert.Contains(NotConfirmed, confirm.Markup, StringComparison.Ordinal);
        Assert.Equal(0, _confirmed);
    }

    [Fact]
    public async Task Confirmed_message_without_server_freshness_does_not_enable_the_action()
    {
        _recent.Recent = false;
        var confirm = RenderConfirm();

        await Report(confirm, Flow(confirm), "confirmed");

        Assert.Equal(0, _confirmed);
        Assert.Contains(NotConfirmed, confirm.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmed_with_server_freshness_is_reported_once()
    {
        var confirm = RenderConfirm();

        await Report(confirm, Flow(confirm), "confirmed");

        Assert.Equal(1, _confirmed);
        Assert.DoesNotContain(NotConfirmed, confirm.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirm_button_carries_a_fresh_flow_per_attempt()
    {
        var confirm = RenderConfirm();
        var flows = new List<string> { Flow(confirm) };

        await Report(confirm, flows[^1], "closed");
        flows.Add(Flow(confirm));
        await Report(confirm, flows[^1], "not-confirmed");
        flows.Add(Flow(confirm));

        Assert.All(flows, f => Assert.Matches(HexFlow(), f));
        Assert.Equal(3, flows.Distinct(StringComparer.Ordinal).Count());
        confirm.WaitForAssertion(() => Assert.Contains(Context.JSInterop.Invocations, i => i.Identifier == "poSession.register" && Equals(i.Arguments[0], flows[^1])));
    }

    [Fact]
    public async Task Blocked_popup_shows_the_link_fallback()
    {
        var confirm = RenderConfirm();
        var flow = Flow(confirm);

        await Report(confirm, flow, "blocked");

        Assert.Contains("Your browser blocked the sign-in window. Allow pop-ups for this site and try again.", confirm.Markup, StringComparison.Ordinal);
        var link = confirm.Find("a");
        Assert.Equal("Open the sign-in window", link.TextContent.Trim());
        Assert.Equal($"/account/confirm?flow={flow}", link.GetAttribute("href"));
        Assert.Equal($"paymentops-confirm-{flow}", link.GetAttribute("target"));
        Assert.Equal(flow, Flow(confirm));
    }

    [Fact]
    public async Task Result_for_another_flow_is_ignored()
    {
        var confirm = RenderConfirm();
        var flow = Flow(confirm);
        var before = confirm.Markup;

        await Report(confirm, new string('a', 32), "confirmed");
        await Report(confirm, new string('b', 32), "closed");

        Assert.Equal(0, _confirmed);
        Assert.Equal(flow, Flow(confirm));
        Assert.Equal(before, confirm.Markup);
    }

    [Fact]
    public async Task Two_components_keep_separate_flows()
    {
        var first = RenderConfirm();
        var second = RenderConfirm();
        var firstFlow = Flow(first);
        var secondFlow = Flow(second);
        Assert.NotEqual(firstFlow, secondFlow);

        await Report(second, firstFlow, "confirmed");
        await Report(first, firstFlow, "closed");

        Assert.Equal(0, _confirmed);
        Assert.Equal(secondFlow, Flow(second));
        Assert.DoesNotContain(NotConfirmed, second.Markup, StringComparison.Ordinal);
        Assert.Contains(NotConfirmed, first.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("different-account", "/signed-out?reason=different-account")]
    [InlineData("access-changed", "/signed-out?reason=access-changed")]
    [InlineData("ended", "/signed-out?reason=inactive")]
    public async Task An_ended_session_reloads_to_signed_out(string result, string target)
    {
        var confirm = RenderConfirm();

        await Report(confirm, Flow(confirm), result);

        var navigation = Assert.Single(Navigations());
        Assert.EndsWith(target, navigation.Uri, StringComparison.Ordinal);
        Assert.True(navigation.Options.ForceLoad);
        Assert.Equal(0, _confirmed);
    }

    // A dead session also needs confirmation; it can't be confirmed back to life, so it goes to signed out.
    [Fact]
    public void A_session_that_is_not_live_is_not_offered_confirm()
    {
        _recent.Live = false;

        var confirm = RenderConfirm();

        Assert.Empty(confirm.FindAll("button[data-po-confirm]"));
        var navigation = Assert.Single(Navigations());
        Assert.EndsWith("/signed-out?reason=inactive", navigation.Uri, StringComparison.Ordinal);
        Assert.True(navigation.Options.ForceLoad);
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex HexFlow();
}
