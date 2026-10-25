using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Tests;

/// <summary>An in-memory index that records each call, and can misbehave the ways Search can.</summary>
internal sealed class FakeKnowledgeIndex : IKnowledgeIndexClient
{
    private readonly Dictionary<string, KnowledgeSearchDocument> _stored = new(StringComparer.Ordinal);
    private Dictionary<string, KnowledgeSearchDocument> _visible = new(StringComparer.Ordinal);

    public List<string> Operations { get; } = [];

    public bool Exists { get; private set; }

    /// <summary>Keys a delete leaves in place.</summary>
    public HashSet<string> StuckKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Index deletes fail once the index has been created, like a cleanup that can't reach Search.</summary>
    public bool FailDeleteOfCreatedIndex { get; set; }

    /// <summary>Thrown by every document delete, like Search refusing a write the identity has no role for.</summary>
    public Exception? DeleteFailure { get; set; }

    /// <summary>How many more key lookups still find every key asked about, like a delete not yet visible.</summary>
    public int LaggingFinds { get; set; }

    /// <summary>How many more full reads return the state from before the latest writes.</summary>
    public int LaggingReads { get; set; }

    /// <summary>Applied to each document a full read returns, like a field changed behind our back.</summary>
    public Func<KnowledgeSearchDocument, KnowledgeSearchDocument> OnRead { get; set; } = d => d;

    public IReadOnlyCollection<KnowledgeSearchDocument> Stored => _stored.Values;

    public FakeKnowledgeIndex With(params KnowledgeSearchDocument[] documents)
    {
        Exists = true;
        foreach (var document in documents)
        {
            _stored[document.Id] = document;
        }

        _visible = new(_stored, StringComparer.Ordinal);
        return this;
    }

    public Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        Operations.Add("ensure");
        Exists = true;
        return Task.CompletedTask;
    }

    public Task DeleteIndexAsync(CancellationToken cancellationToken)
    {
        Operations.Add("delete-index");
        if (FailDeleteOfCreatedIndex && Exists)
        {
            throw new KnowledgePublishException("Deleting the index failed.");
        }

        Exists = false;
        _stored.Clear();
        _visible.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KnowledgeSearchDocument>> ReadAllAsync(CancellationToken cancellationToken)
    {
        Operations.Add("read");
        if (LaggingReads > 0)
        {
            LaggingReads--;
        }
        else
        {
            _visible = new(_stored, StringComparer.Ordinal);
        }

        return Task.FromResult<IReadOnlyList<KnowledgeSearchDocument>>([.. _visible.Values.Select(OnRead)]);
    }

    public Task<IReadOnlyList<string>> FindExistingAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        Operations.Add("find:" + Join(keys));
        if (LaggingFinds > 0)
        {
            LaggingFinds--;
            return Task.FromResult<IReadOnlyList<string>>([.. keys]);
        }

        return Task.FromResult<IReadOnlyList<string>>([.. keys.Where(_stored.ContainsKey)]);
    }

    public Task UploadAsync(IReadOnlyCollection<KnowledgeSearchDocument> documents, CancellationToken cancellationToken)
    {
        Operations.Add("upload:" + Join(documents.Select(d => d.Id)));
        foreach (var document in documents)
        {
            _stored[document.Id] = document;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        Operations.Add("delete:" + Join(keys));
        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        foreach (var key in keys.Where(k => !StuckKeys.Contains(k)))
        {
            _stored.Remove(key);
        }

        return Task.CompletedTask;
    }

    private static string Join(IEnumerable<string> keys) => string.Join(",", keys.Order(StringComparer.Ordinal));
}
