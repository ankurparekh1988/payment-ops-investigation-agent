using System.Text.RegularExpressions;

namespace PaymentOps.Platform.Text;

/// <summary>
/// Everything the build ships lives in a public repository, so nothing in it may look like real payment or
/// contact data: no number that passes the card-number checksum, and only reserved example domains.
/// </summary>
public static partial class PublicContentCheck
{
    // RFC 2606 names: example.com, .net and .org, and the .example top-level domain.
    private static readonly string[] ExampleDomains = ["example.com", "example.net", "example.org"];

    public static void Check(string body, string path, int firstLine, List<string> errors)
    {
        string Where(int index) => $"{path}:{firstLine + body.AsSpan(0, index).Count('\n')}";

        foreach (Match match in CardLikeNumber().Matches(body))
        {
            if (ContainsCardNumber(match.Value))
            {
                errors.Add($"{Where(match.Index)}: '{match.Value}' passes the card-number checksum; use a masked value such as '•••• 4242'.");
            }
        }

        foreach (Match match in EmailAddress().Matches(body))
        {
            if (!IsExampleDomain(match.Groups["domain"].Value))
            {
                errors.Add($"{Where(match.Index)}: email '{match.Value}' must use a reserved example domain such as example.com.");
            }
        }

        foreach (Match match in WebAddress().Matches(body))
        {
            if (!IsExampleDomain(match.Groups["host"].Value))
            {
                errors.Add($"{Where(match.Index)}: URL '{match.Value}' must use a reserved example domain such as example.com.");
            }
        }
    }

    internal static bool IsExampleDomain(string domain)
    {
        var host = domain.TrimEnd('.').ToLowerInvariant();
        return host.EndsWith(".example", StringComparison.Ordinal)
            || ExampleDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    internal static bool PassesLuhn(int[] digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    // A card number is written whole or in groups, so every run of whole groups holding 13 to 19
    // digits is checked. A card next to another number (an expiry, a reference) is still found.
    private static bool ContainsCardNumber(string run)
    {
        var groups = run.Split([' ', '-']);
        for (var first = 0; first < groups.Length; first++)
        {
            var digits = new List<int>();
            for (var last = first; last < groups.Length && digits.Count < 19; last++)
            {
                digits.AddRange(groups[last].Select(c => c - '0'));
                if (digits.Count is >= 13 and <= 19 && PassesLuhn([.. digits]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // At least 13 digits, optionally grouped with spaces or hyphens, as card numbers are written. A
    // reference number can be mistaken for a card; a false alarm is cheaper than a missed number.
    [GeneratedRegex(@"(?<!\d)\d(?:[ -]?\d){12,}(?!\d)")]
    private static partial Regex CardLikeNumber();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@(?<domain>[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\b(?:https?|ftp)://(?<host>[A-Za-z0-9.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();
}
