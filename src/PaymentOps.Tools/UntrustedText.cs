namespace PaymentOps.Tools;

/// <summary>
/// Marks text written outside this system (names, notes, references, codes, knowledge) as data for the model. The
/// marker also tells the investigation which values to screen for instructions before the model reads them.
/// </summary>
public static class UntrustedText
{
    public const string Open = "<untrusted-data>";
    public const string Close = "</untrusted-data>";

    // A closing marker inside the text would end the block early, so it's neutralised first.
    public static string Wrap(string text) =>
        Open + (text ?? string.Empty).Replace(Close, "</untrusted-data-escaped>", StringComparison.OrdinalIgnoreCase) + Close;

    /// <summary>Whether the value is exactly one marked segment, as <see cref="Wrap"/> writes it.</summary>
    public static bool IsWrapped(string value) =>
        value.Length >= Open.Length + Close.Length
        && value.StartsWith(Open, StringComparison.Ordinal)
        && value.EndsWith(Close, StringComparison.Ordinal)
        && value.IndexOf(Close, Open.Length, StringComparison.OrdinalIgnoreCase) == value.Length - Close.Length;

    /// <summary>The text inside a marked segment.</summary>
    public static string Unwrap(string value) => value[Open.Length..^Close.Length];
}
