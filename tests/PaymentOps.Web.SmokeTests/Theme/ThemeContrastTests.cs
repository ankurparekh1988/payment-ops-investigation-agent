using System.Globalization;

using PaymentOps.Web.Theme;

namespace PaymentOps.Web.SmokeTests.Theme;

/// <summary>Every text colour the theme defines is readable on the background it's used on (WCAG 2.2 AA, 4.5:1).</summary>
public sealed class ThemeContrastTests
{
    public static TheoryData<string, string, string> Pairs()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (name, foreground, background) in ContosoTheme.TextPairs)
        {
            data.Add(name, foreground, background);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Every_text_pair_meets_AA(string name, string foreground, string background)
    {
        var ratio = ContrastRatio(foreground, background);

        Assert.True(ratio >= 4.5, $"{name}: {foreground} on {background} is {ratio:F2}:1");
    }

    [Fact]
    public void Both_modes_define_their_pairs()
    {
        Assert.Contains(ContosoTheme.TextPairs, pair => pair.Name.StartsWith("light ", StringComparison.Ordinal));
        Assert.Contains(ContosoTheme.TextPairs, pair => pair.Name.StartsWith("dark ", StringComparison.Ordinal));
    }

    private static double ContrastRatio(string first, string second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var rgb = int.Parse(hex.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (0.2126 * Channel(rgb >> 16)) + (0.7152 * Channel(rgb >> 8)) + (0.0722 * Channel(rgb));

        static double Channel(int value)
        {
            var c = (value & 0xFF) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }
}
