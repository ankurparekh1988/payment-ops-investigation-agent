namespace PaymentOps.Knowledge.Indexing;

/// <summary>One search index, as the publisher needs it. Keeps the Search SDK behind one adapter.</summary>
public interface IKnowledgeIndexClient
{
    /// <summary>Creates the index, or updates it to the current schema.</summary>
    Task EnsureIndexAsync(CancellationToken cancellationToken);

    /// <summary>Deletes the index; a missing index is not an error.</summary>
    Task DeleteIndexAsync(CancellationToken cancellationToken);

    /// <summary>Every stored document, with every field.</summary>
    Task<IReadOnlyList<KnowledgeSearchDocument>> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>The keys from <paramref name="keys"/> that a lookup by key still finds.</summary>
    Task<IReadOnlyList<string>> FindExistingAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    Task UploadAsync(IReadOnlyCollection<KnowledgeSearchDocument> documents, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
