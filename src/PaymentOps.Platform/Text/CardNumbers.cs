using System.Text.RegularExpressions;

namespace PaymentOps.Platform.Text;

/// <summary>Numbers that pass the card-number checksum, written whole or in groups of digits.</summary>
public static partial class CardNumbers
{
    /// <summary>Each run of digits that holds a card number, with where it starts.</summary>
    public static IEnumerable<(int Index, string Value)> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (Match match in CardLikeNumber().Matches(text))
        {
            if (ContainsCardNumber(match.Value))
            {
                yield return (match.Index, match.Value);
            }
        }
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
}
