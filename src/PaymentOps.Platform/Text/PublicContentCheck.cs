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

        foreach (var (index, value) in CardNumbers.Find(body))
        {
            errors.Add($"{Where(index)}: '{value}' passes the card-number checksum; use a masked value such as '•••• 4242'.");
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

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@(?<domain>[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\b(?:https?|ftp)://(?<host>[A-Za-z0-9.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();
}
