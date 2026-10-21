namespace PaymentOps.Tools;

/// <summary>
/// Marks text written outside this system (incident summaries, knowledge content) as data for the model.
/// Guidance, not a control: the structural controls are read-only tools and server-side validation.
/// </summary>
public static class UntrustedText
{
    private const string Open = "<untrusted-data>";
    private const string Close = "</untrusted-data>";

    // A closing marker inside the text would end the block early, so it's neutralised first.
    public static string Wrap(string text) =>
        Open + (text ?? string.Empty).Replace(Close, "</untrusted-data-escaped>", StringComparison.OrdinalIgnoreCase) + Close;
}
