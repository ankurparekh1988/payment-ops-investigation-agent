namespace PaymentOps.Tools;

/// <summary>Limits on what one tool call returns. Defaults keep a result near 2,000 tokens.</summary>
public sealed class ToolOptions
{
    public const string SectionName = "PaymentOps:Tools";

    /// <summary>Items shown per list; facts are still computed over everything read.</summary>
    public int MaxListItems { get; set; } = 20;

    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    public int MaxQueryLength { get; set; } = 300;
}
