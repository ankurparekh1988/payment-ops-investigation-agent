namespace PaymentOps.Agent.Telemetry;

/// <summary>
/// Content Safety bills text by the record: up to 1,000 characters, counted in Unicode code points, so longer text
/// is one record per started thousand. Empty text analyses nothing and counts as none.
/// </summary>
internal static class TextRecords
{
    private const int CharactersPerRecord = 1000;

    public static int For(string? text)
    {
        var codePoints = string.IsNullOrEmpty(text) ? 0 : text.EnumerateRunes().Count();
        return (codePoints + CharactersPerRecord - 1) / CharactersPerRecord;
    }
}
