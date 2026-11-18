using System.Collections.Concurrent;

using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// The raw output of reads that succeeded in this investigation, so a second attempt doesn't call the source again.
/// It holds no verdict and nothing registered: a hit replaces only the source call, and the output is screened and
/// registered again in the attempt that reads it (ADR 0023). One per investigation, so no read crosses investigations.
/// </summary>
internal sealed class ReadCache
{
    /// <summary>How hits are counted in the turn's usage and metrics.</summary>
    public const string Name = "read";

    private readonly ConcurrentDictionary<string, ToolResult.Ok> _reads = new(StringComparer.Ordinal);

    public bool TryGet(string key, out ToolResult.Ok result) => _reads.TryGetValue(key, out result!);

    public void Remember(string key, ToolResult.Ok result) => _reads.TryAdd(key, result);
}
