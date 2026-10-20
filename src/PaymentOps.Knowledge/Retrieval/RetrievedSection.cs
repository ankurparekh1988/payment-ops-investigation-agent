namespace PaymentOps.Knowledge.Retrieval;

/// <summary>A section returned for a question. Classification is for checks and diagnostics, never the prompt.</summary>
/// <param name="RerankerScore">The semantic reranker score (0-4); null for lexical queries.</param>
public sealed record RetrievedSection(string Key, string DocId, string Title, string SectionPath, string Content, string Classification, double? RerankerScore);
