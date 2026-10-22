using System.Text.RegularExpressions;

using PaymentOps.Platform.Text;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Blocks card-like and account-like numbers in ticket text; it isn't bank-detail DLP. Reports rule ids only,
/// so a matched value never reaches the model, a log or the page.
/// </summary>
internal static partial class TicketContentPolicy
{
    public const string CardNumber = "card_number";
    public const string AccountNumber = "account_number";

    public static IReadOnlyList<string> Violations(string text)
    {
        List<string> violations = [];
        if (CardNumbers.Find(text).Any())
        {
            violations.Add(CardNumber);
        }

        // No dataset id or reference has eight digits in a row, so any such run is treated as an account number.
        if (LongDigitRun().IsMatch(text) || Iban().IsMatch(text))
        {
            violations.Add(AccountNumber);
        }

        return violations;
    }

    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex LongDigitRun();

    // Written whole or in groups of four, in either case.
    [GeneratedRegex(@"\b[A-Za-z]{2}\d{2}(?: ?[A-Za-z0-9]){11,30}\b")]
    private static partial Regex Iban();
}
