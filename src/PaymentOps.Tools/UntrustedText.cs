namespace PaymentOps.Tools;

/// <summary>
/// Marks text written outside this system (names, notes, references, codes, knowledge) as data for the model. The
/// marker also tells the investigation which values to screen for instructions before the model reads them.
/// </summary>
public static class UntrustedText
{
    public const string Open = "<untrusted-data>";
    public const string Close = "</untrusted-data>";

    // Labels are screened like any untrusted text, then read as the plain value when clean.
    public const string LabelOpen = "<untrusted-label>";
    public const string LabelClose = "</untrusted-label>";

    // A closing marker inside the text would end the block early, so it's neutralised first.
    public static string Wrap(string text) => Mark(text, Open, Close);

    /// <summary>
    /// Marks a short external value (a name, a code, a reference) for screening. The model reads it plain once it's
    /// screened clean: markers around names changed which processor and dates the model went on to ask about.
    /// </summary>
    public static string Label(string text) => Mark(text, LabelOpen, LabelClose);

    /// <summary>Whether the value is exactly one marked segment, as <see cref="Wrap"/> writes it.</summary>
    public static bool IsWrapped(string value) => IsMarked(value, Open, Close);

    /// <summary>Whether the value is exactly one label, as <see cref="Label"/> writes it.</summary>
    public static bool IsLabel(string value) => IsMarked(value, LabelOpen, LabelClose);

    /// <summary>The text inside a marked segment or a label.</summary>
    public static string Unwrap(string value) =>
        IsLabel(value) ? value[LabelOpen.Length..^LabelClose.Length] : value[Open.Length..^Close.Length];

    private static string Mark(string text, string open, string close) =>
        open + (text ?? string.Empty).Replace(close, close[..^1] + "-escaped>", StringComparison.OrdinalIgnoreCase) + close;

    private static bool IsMarked(string value, string open, string close) =>
        value.Length >= open.Length + close.Length
        && value.StartsWith(open, StringComparison.Ordinal)
        && value.EndsWith(close, StringComparison.Ordinal)
        && value.IndexOf(close, open.Length, StringComparison.OrdinalIgnoreCase) == value.Length - close.Length;
}
