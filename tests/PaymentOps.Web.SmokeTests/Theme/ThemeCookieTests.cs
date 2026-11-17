using PaymentOps.Web.Theme;

namespace PaymentOps.Web.SmokeTests.Theme;

/// <summary>
/// The server prerenders the person's chosen mode, else the OS preference the browser reported, else light; the page
/// carries the resolved mode on interactive and static pages alike.
/// </summary>
public sealed class ThemeCookieTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Theory]
    [InlineData("light", null, false, true)]
    [InlineData("dark", null, true, true)]
    [InlineData(null, null, false, false)]
    [InlineData(null, "dark", true, false)]
    [InlineData(null, "light", false, false)]
    [InlineData("light", "dark", false, true)]
    [InlineData("purple", "dark", true, false)]
    [InlineData("<script>", "nonsense", false, false)]
    public void Resolve_prefers_the_choice_then_the_system_then_light(string? chosen, string? system, bool dark, bool isChosen)
    {
        var preference = ThemeCookie.Resolve(chosen, system);

        Assert.Equal(new ThemePreference(dark, isChosen), preference);
    }

    [Theory]
    [InlineData("/", "po-theme=dark", "dark")]
    [InlineData("/", "po-theme-system=dark", "dark")]
    [InlineData("/", "po-theme=light; po-theme-system=dark", "light")]
    [InlineData("/", "", "light")]
    [InlineData("/signed-out", "po-theme=dark", "dark")]
    [InlineData("/signed-out", "po-theme-system=light", "light")]
    public async Task The_page_is_prerendered_in_the_resolved_mode(string path, string cookies, string mode)
    {
        using var client = factory.CreateReaderClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (cookies.Length > 0)
        {
            request.Headers.Add("Cookie", cookies);
        }

        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains($"<html lang=\"en\" data-theme=\"{mode}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_theme_script_loads_from_the_app_before_the_stylesheets()
    {
        using var client = factory.CreateReaderClient();

        var html = await client.GetStringAsync(new Uri("/signed-out", UriKind.Relative));

        var script = html.IndexOf("js/theme-init", StringComparison.Ordinal);
        Assert.True(script > 0, "theme-init.js is referenced");
        Assert.True(script < html.IndexOf("stylesheet", StringComparison.Ordinal), "it runs before the first stylesheet");
        Assert.DoesNotContain("fonts.googleapis", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_theme_script_is_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/js/theme-init.js", UriKind.Relative));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }
}
