using System.Text.RegularExpressions;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// The merchants the user named in this conversation's questions. Only these may be looked up, so an id planted in a
/// record or a runbook can't send the investigation to another merchant even when the screen missed it.
/// </summary>
internal sealed partial class MerchantSources
{
    private readonly HashSet<string> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="questions">What the user typed, this turn and earlier ones; never model or tool text.</param>
    public MerchantSources(IEnumerable<string> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        foreach (var token in questions.SelectMany(q => Token().Matches(q ?? string.Empty)))
        {
            // A sentence's full stop or a joining hyphen isn't part of an id.
            _named.Add(token.Value.TrimEnd('.', '-', '_'));
        }
    }

    // People write ids in another case than the records use; the tools then report it as not found.
    public bool Named(string merchantId) => _named.Contains(merchantId.Trim());

    [GeneratedRegex("[A-Za-z0-9][A-Za-z0-9._-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
