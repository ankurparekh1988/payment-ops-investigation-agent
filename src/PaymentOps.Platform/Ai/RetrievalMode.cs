namespace PaymentOps.Platform.Ai;

/// <summary>How the knowledge index is queried. The release chooses; requests never do.</summary>
public enum RetrievalMode
{
    Lexical,
    Semantic,
}
