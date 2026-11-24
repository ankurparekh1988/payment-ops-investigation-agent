using Bunit;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Components.Layout;

namespace PaymentOps.Web.SmokeTests;

/// <summary>The shell says whose app it is, who is signed in and at what level, and which environment this is.</summary>
public sealed class ShellTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task The_header_shows_the_company_the_user_and_their_highest_role()
    {
        using var client = factory.CreateClientFor("Ada Admin", [OpsRoles.Admin]);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("Calderbrook Payments", html, StringComparison.Ordinal);
        Assert.Contains("Payment Operations", html, StringComparison.Ordinal);
        Assert.Contains("Ada Admin", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"user-role\">Admin</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Risk and Compliance", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_header_shows_risk_and_compliance_for_a_member_of_the_group()
    {
        using var client = factory.CreateClientFor("Rita Risk", [OpsRoles.Reader], PaymentOpsWebApplicationFactory.RestrictedGroupId);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<span class=\"user-role\">Reader</span>", html, StringComparison.Ordinal);
        Assert.Contains("Risk and Compliance", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_environment_badge_is_hidden_when_not_configured()
    {
        using var client = factory.CreateReaderClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.DoesNotContain("environment-badge", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_environment_badge_shows_the_configured_label()
    {
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("PaymentOps:Ui:EnvironmentLabel", "Demo environment"));
        using var client = PaymentOpsWebApplicationFactory.SignedIn(app, "Riley Reader", [OpsRoles.Reader]);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("environment-badge", html, StringComparison.Ordinal);
        Assert.Contains("Demo environment", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_navigation_and_sign_out_are_present()
    {
        using var client = factory.CreateReaderClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("href=\"investigate\"", html, StringComparison.Ordinal);
        Assert.Contains("action=\"account/sign-out\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
    }
}

/// <summary>The theme toggle names what it does and records the person's choice.</summary>
public sealed class ThemeToggleTests : InteractiveTestContext
{
    [Theory]
    [InlineData(false, "Switch to dark theme")]
    [InlineData(true, "Switch to light theme")]
    public void The_toggle_has_an_accessible_name(bool dark, string name)
    {
        var toggle = Render<ThemeToggle>(p => p.Add(t => t.IsDark, dark));

        Assert.Equal(name, toggle.Find("button").GetAttribute("aria-label"));
    }

    [Fact]
    public void Toggling_records_the_choice_and_reports_the_new_mode()
    {
        bool? reported = null;
        var toggle = Render<ThemeToggle>(p => p
            .Add(t => t.IsDark, false)
            .Add(t => t.IsDarkChanged, value => reported = value));

        toggle.Find("button").Click();

        Assert.True(reported);
        Assert.Equal("dark", Context.JSInterop.VerifyInvoke("poTheme.set").Arguments.Single());
    }
}
