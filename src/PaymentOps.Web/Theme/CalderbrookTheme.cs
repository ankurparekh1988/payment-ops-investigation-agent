using MudBlazor;

namespace PaymentOps.Web.Theme;

/// <summary>
/// The Calderbrook Payments theme for both modes. Status colours are dark enough to be read as text, not only as fills,
/// and every text colour is listed in <see cref="TextPairs"/> with the background it's used on, so contrast is tested.
/// </summary>
public static class CalderbrookTheme
{
    public const string FontStack = "Inter, \"Segoe UI\", system-ui, -apple-system, sans-serif";

    private static class Light
    {
        public const string AppBar = "#0F172A";
        public const string AppBarText = "#F8FAFC";
        public const string AppBarMuted = "#94A3B8";
        public const string Background = "#F3F5F8";
        public const string Surface = "#FFFFFF";
        public const string Text = "#0F172A";
        public const string TextSecondary = "#475569";
        public const string Border = "#E2E8F0";
        public const string Primary = "#1D4ED8";
        public const string Success = "#15803D";
        public const string Warning = "#B45309";
        public const string Error = "#B91C1C";
        public const string OnColour = "#FFFFFF";
    }

    private static class Dark
    {
        public const string AppBar = "#0F172A";
        public const string AppBarText = "#F8FAFC";
        public const string AppBarMuted = "#94A3B8";
        public const string Background = "#0B1220";
        public const string Surface = "#111A2E";
        public const string Text = "#E2E8F0";
        public const string TextSecondary = "#94A3B8";
        public const string Border = "#1E293B";

        // #2563EB reads at only 3.4:1 as text on the dark surface, so dark mode uses a lighter blue with dark text on it.
        public const string Primary = "#60A5FA";
        public const string Success = "#4ADE80";
        public const string Warning = "#FBBF24";
        public const string Error = "#F87171";
        public const string OnColour = "#0B1220";
    }

    /// <summary>The app bar's secondary text ("Payment Operations"), outside MudBlazor's palette.</summary>
    public const string AppBarMutedText = Light.AppBarMuted;

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            AppbarBackground = Light.AppBar,
            AppbarText = Light.AppBarText,
            Background = Light.Background,
            Surface = Light.Surface,
            DrawerBackground = Light.Surface,
            TextPrimary = Light.Text,
            TextSecondary = Light.TextSecondary,
            Primary = Light.Primary,
            PrimaryContrastText = Light.OnColour,
            Secondary = Light.TextSecondary,
            SecondaryContrastText = Light.OnColour,
            Info = Light.Primary,
            InfoContrastText = Light.OnColour,
            Success = Light.Success,
            SuccessContrastText = Light.OnColour,
            Warning = Light.Warning,
            WarningContrastText = Light.OnColour,
            Error = Light.Error,
            ErrorContrastText = Light.OnColour,
            LinesDefault = Light.Border,
            TableLines = Light.Border,
            Divider = Light.Border,
        },
        PaletteDark = new PaletteDark
        {
            AppbarBackground = Dark.AppBar,
            AppbarText = Dark.AppBarText,
            Background = Dark.Background,
            Surface = Dark.Surface,
            DrawerBackground = Dark.Surface,
            TextPrimary = Dark.Text,
            TextSecondary = Dark.TextSecondary,
            Primary = Dark.Primary,
            PrimaryContrastText = Dark.OnColour,
            Secondary = Dark.TextSecondary,
            SecondaryContrastText = Dark.OnColour,
            Info = Dark.Primary,
            InfoContrastText = Dark.OnColour,
            Success = Dark.Success,
            SuccessContrastText = Dark.OnColour,
            Warning = Dark.Warning,
            WarningContrastText = Dark.OnColour,
            Error = Dark.Error,
            ErrorContrastText = Dark.OnColour,
            LinesDefault = Dark.Border,
            TableLines = Dark.Border,
            Divider = Dark.Border,
        },
        // One scale for the whole app: 14px body, 600 for headings and actions, sentence-case buttons.
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = [.. FontStack.Split(", ")], FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "normal" },
            H1 = new H1Typography { FontSize = "1.375rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "normal" },
            H2 = new H2Typography { FontSize = "1rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "normal" },
            H3 = new H3Typography { FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "normal" },
            Body1 = new Body1Typography { FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "normal" },
            Body2 = new Body2Typography { FontSize = "0.8125rem", FontWeight = "400", LineHeight = "1.5", LetterSpacing = "normal" },
            Caption = new CaptionTypography { FontSize = "0.75rem", FontWeight = "500", LineHeight = "1.4", LetterSpacing = "normal" },
            Button = new ButtonTypography { FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.5", LetterSpacing = "normal", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px" },
    };

    /// <summary>Every colour the app may use; the stylesheet is tested against this list.</summary>
    public static IReadOnlySet<string> Colours { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Light.AppBar, Light.AppBarText, Light.AppBarMuted, Light.Background, Light.Surface, Light.Text, Light.TextSecondary,
        Light.Border, Light.Primary, Light.Success, Light.Warning, Light.Error, Light.OnColour,
        Dark.Background, Dark.Surface, Dark.Text, Dark.TextSecondary, Dark.Border, Dark.Primary, Dark.Success, Dark.Warning,
        Dark.Error, Dark.OnColour,
    };

    /// <summary>Every text colour the theme uses, on each background it appears on, per mode.</summary>
    public static IReadOnlyList<(string Name, string Foreground, string Background)> TextPairs { get; } =
    [
        .. Pairs("light", Light.Text, Light.TextSecondary, Light.Primary, Light.Success, Light.Warning, Light.Error,
            Light.OnColour, Light.AppBarText, Light.AppBarMuted, Light.AppBar, [Light.Background, Light.Surface]),
        .. Pairs("dark", Dark.Text, Dark.TextSecondary, Dark.Primary, Dark.Success, Dark.Warning, Dark.Error,
            Dark.OnColour, Dark.AppBarText, Dark.AppBarMuted, Dark.AppBar, [Dark.Background, Dark.Surface, Dark.Border]),
    ];

    private static IEnumerable<(string, string, string)> Pairs(
        string mode, string text, string secondary, string primary, string success, string warning, string error,
        string onColour, string appBarText, string appBarMuted, string appBar, string[] backgrounds)
    {
        foreach (var background in backgrounds)
        {
            yield return ($"{mode} text on {background}", text, background);
            yield return ($"{mode} secondary text on {background}", secondary, background);
            yield return ($"{mode} primary on {background}", primary, background);
            yield return ($"{mode} success on {background}", success, background);
            yield return ($"{mode} warning on {background}", warning, background);
            yield return ($"{mode} error on {background}", error, background);
        }

        foreach (var fill in new[] { primary, success, warning, error })
        {
            yield return ($"{mode} text on {fill} fill", onColour, fill);
        }

        yield return ($"{mode} app bar text", appBarText, appBar);
        yield return ($"{mode} app bar secondary text", appBarMuted, appBar);
    }
}
