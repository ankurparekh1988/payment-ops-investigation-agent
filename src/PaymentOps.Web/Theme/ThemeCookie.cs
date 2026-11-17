using Microsoft.AspNetCore.Http;

namespace PaymentOps.Web.Theme;

/// <summary>The mode to render, and whether the person chose it (else it follows their OS).</summary>
public sealed record ThemePreference(bool IsDark, bool IsChosen)
{
    public string Mode => IsDark ? ThemeCookie.Dark : ThemeCookie.Light;
}

/// <summary>
/// Reads the theme cookies written by <c>js/theme-init.js</c>: <c>po-theme</c> when the person toggled, and
/// <c>po-theme-system</c> with the OS preference otherwise. The server can't see the OS preference itself.
/// </summary>
public static class ThemeCookie
{
    public const string ChosenName = "po-theme";
    public const string SystemName = "po-theme-system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static ThemePreference Resolve(IRequestCookieCollection cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        return Resolve(cookies[ChosenName], cookies[SystemName]);
    }

    public static ThemePreference Resolve(string? chosen, string? system) => chosen switch
    {
        Dark or Light => new ThemePreference(chosen == Dark, IsChosen: true),
        _ => new ThemePreference(system == Dark, IsChosen: false),
    };
}
