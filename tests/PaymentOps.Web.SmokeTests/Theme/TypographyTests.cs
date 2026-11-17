using System.Text.RegularExpressions;

using MudBlazor;
using MudBlazor.Utilities;

using PaymentOps.Web.Theme;

namespace PaymentOps.Web.SmokeTests.Theme;

/// <summary>
/// One typeface, served by the app, and one set of colours: the stylesheet may use only colours the theme defines,
/// so a page can't drift from the palette the contrast tests check.
/// </summary>
public sealed partial class TypographyTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task Inter_is_served_by_the_app_and_preloaded()
    {
        using var client = factory.CreateReaderClient();

        using var font = await client.GetAsync(new Uri("/fonts/InterVariable.woff2", UriKind.Relative));
        var html = await client.GetStringAsync(new Uri("/signed-out", UriKind.Relative));

        Assert.True(font.IsSuccessStatusCode);
        Assert.Equal("font/woff2", font.Content.Headers.ContentType?.MediaType);
        Assert.Matches("<link rel=\"preload\" href=\"[^\"]*fonts/InterVariable[^\"]*woff2\" as=\"font\" type=\"font/woff2\" crossorigin", html);
    }

    [Fact]
    public async Task The_stylesheet_uses_only_theme_colours()
    {
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync(new Uri("/app.css", UriKind.Relative));

        var stray = HexColour().Matches(css)
            .Select(match => match.Value.ToUpperInvariant())
            .Where(colour => !ContosoTheme.Colours.Contains(colour))
            .Distinct()
            .ToList();
        Assert.Empty(stray);
    }

    // Static pages never start MudBlazor, so the stylesheet switches the palette with the page's mode: every variable it
    // sets must be the theme's own dark value, and the colours text and status chips use must all be there.
    [Fact]
    public async Task The_stylesheet_dark_palette_is_the_themes()
    {
        using var client = factory.CreateClient();
        var css = await client.GetStringAsync(new Uri("/app.css", UriKind.Relative));
        var start = css.IndexOf("html[data-theme=\"dark\"] {", StringComparison.Ordinal);
        var block = css[start..css.IndexOf('}', start)];
        var set = DarkVariable().Matches(block).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        var dark = ContosoTheme.Theme.PaletteDark;
        var expected = new Dictionary<string, MudColor>(StringComparer.Ordinal)
        {
            ["background"] = dark.Background,
            ["surface"] = dark.Surface,
            ["text-primary"] = dark.TextPrimary,
            ["text-secondary"] = dark.TextSecondary,
            ["lines-default"] = dark.LinesDefault,
            ["primary"] = dark.Primary,
            ["primary-text"] = dark.PrimaryContrastText,
            ["success"] = dark.Success,
            ["warning"] = dark.Warning,
            ["error"] = dark.Error,
        };

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), set.Keys.Order(StringComparer.Ordinal));
        Assert.All(expected, pair => Assert.Equal(pair.Value.ToString(MudColorOutputFormats.Hex), set[pair.Key], ignoreCase: true));
    }

    [GeneratedRegex("--mud-palette-([a-z-]+):\\s*(#[0-9A-Fa-f]{6})")]
    private static partial Regex DarkVariable();

    [Fact]
    public void The_theme_sets_inter_and_sentence_case_buttons()
    {
        Assert.Equal("Inter", ContosoTheme.Theme.Typography.Default.FontFamily![0]);
        Assert.Equal("none", ContosoTheme.Theme.Typography.Button.TextTransform);
    }

    [GeneratedRegex("#[0-9A-Fa-f]{6}\\b")]
    private static partial Regex HexColour();
}
