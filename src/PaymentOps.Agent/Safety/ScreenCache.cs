using System.Security.Cryptography;
using System.Text;

namespace PaymentOps.Agent.Safety;

/// <summary>
/// Verdicts for text already screened, keyed by its SHA-256. Knowledge and names repeat across investigations, so
/// most text is screened once. Least recently used entries go first; an unavailable verdict is never kept.
/// </summary>
internal sealed class ScreenCache(int capacity = 2000)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, ScreenVerdict Verdict)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, ScreenVerdict Verdict)> _recency = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(string text, out ScreenVerdict verdict)
    {
        var key = Key(text);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                verdict = node.Value.Verdict;
                return true;
            }
        }

        verdict = default;
        return false;
    }

    public void Set(string text, ScreenVerdict verdict)
    {
        if (verdict == ScreenVerdict.Unavailable)
        {
            return;
        }

        var key = Key(text);
        lock (_lock)
        {
            if (_entries.Remove(key, out var existing))
            {
                _recency.Remove(existing);
            }
            else if (_entries.Count >= capacity && _recency.Last is { } oldest)
            {
                _entries.Remove(oldest.Value.Key);
                _recency.RemoveLast();
            }

            _entries[key] = _recency.AddFirst((key, verdict));
        }
    }

    private static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
