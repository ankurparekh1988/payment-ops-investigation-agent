using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Components.Layout;
using PaymentOps.Web.Theme;

namespace PaymentOps.Web.SmokeTests.Theme;

/// <summary>
/// On a first visit the server can't know the OS preference, so it prerenders light; once interactive, the layout
/// takes the mode the head script recorded on the page, so a dark OS doesn't keep light panels on a dark page.
/// </summary>
public sealed class LayoutThemeTests : InteractiveTestContext
{
    public LayoutThemeTests()
    {
        Context.Services.AddSingleton<IUserContext>(TestUser.Reader);
        Context.Services.AddSingleton(Options.Create(new UiOptions()));
    }

    private IRenderedComponent<MainLayout> Layout(ThemePreference preference) =>
        Render<MainLayout>(p => p
            .AddCascadingValue(preference)
            .Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

    [Fact]
    public void A_page_the_browser_marked_dark_turns_the_layout_dark()
    {
        Context.JSInterop.Setup<string>("poTheme.mode").SetResult(ThemeCookie.Dark);

        var layout = Layout(new ThemePreference(IsDark: false, IsChosen: false));

        layout.WaitForAssertion(() => Assert.True(DarkSurface(layout.Markup), "the dark surface colour is in use"));
    }

    [Fact]
    public void A_chosen_mode_is_kept_whatever_the_page_reports()
    {
        Context.JSInterop.Setup<string>("poTheme.mode").SetResult(ThemeCookie.Dark);

        var layout = Layout(new ThemePreference(IsDark: false, IsChosen: true));

        Assert.False(DarkSurface(layout.Markup));
        Assert.DoesNotContain(Context.JSInterop.Invocations, i => i.Identifier == "poTheme.mode");
    }

    // The theme provider writes the palette as CSS variables, as hex or rgb depending on the colour.
    private static bool DarkSurface(string markup)
    {
        var compact = markup.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return compact.Contains("--MUD-PALETTE-SURFACE:#111A2E", StringComparison.Ordinal)
            || compact.Contains("--MUD-PALETTE-SURFACE:RGBA(17,26,46", StringComparison.Ordinal)
            || compact.Contains("--MUD-PALETTE-SURFACE:RGB(17,26,46", StringComparison.Ordinal);
    }
}
