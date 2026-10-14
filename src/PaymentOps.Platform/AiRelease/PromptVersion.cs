using System.Globalization;
using System.Text.RegularExpressions;

namespace PaymentOps.Platform.AiRelease;

/// <summary>A strict MAJOR.MINOR.PATCH version: no prefixes, pre-release tags or leading zeros.</summary>
internal readonly partial record struct PromptVersion(int Major, int Minor, int Patch)
{
    public static bool TryParse(string? text, out PromptVersion version)
    {
        var match = text is null ? null : Pattern().Match(text);
        if (match is not { Success: true })
        {
            version = default;
            return false;
        }

        version = new PromptVersion(Part(match, 1), Part(match, 2), Part(match, 3));
        return true;
    }

    public bool IsHigherThan(PromptVersion other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch)) > 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    private static int Part(Match match, int group) => int.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
