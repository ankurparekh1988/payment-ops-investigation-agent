using System.Collections.Immutable;
using System.Diagnostics;

using PaymentOps.Agent.Investigating;

namespace PaymentOps.Agent.Telemetry;

/// <summary>
/// What one turn used, kept apart by what it is: requests sent (retries and failures included), Content Safety text
/// records answered, model tokens per deployment, and cache hits that sent nothing. Every measurement is metered;
/// inside a turn it's also added here. Usage is incomplete when a response carried no usage or a request's outcome
/// is unknown, and then no cost is estimated from it.
/// </summary>
internal sealed class TurnUsage : IDisposable
{
    private static readonly AsyncLocal<TurnUsage?> Ambient = new();

    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _httpRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Input, long Output)> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _cachedInput = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _modelCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _modelHttp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _cacheHits = new(StringComparer.Ordinal);
    private readonly List<string> _incomplete = [];
    private readonly List<(string Name, string Outcome)> _tools = [];
    private readonly Dictionary<string, int> _screens = new(StringComparer.Ordinal);
    private readonly List<AttemptRecord> _attempts = [];
    private string? _escalation;
    private string? _routing;
    private int _estimateExceeded;
    private TurnUsage? _previous;

    public static TurnUsage? Current => Ambient.Value;

    /// <summary>Starts collecting for the current turn until disposed.</summary>
    public static TurnUsage Begin()
    {
        var usage = new TurnUsage { _previous = Ambient.Value };
        Ambient.Value = usage;
        return usage;
    }

    public void Dispose() => Ambient.Value = _previous;

    public IReadOnlyDictionary<string, int> HttpRequests => Snapshot(_httpRequests);

    public IReadOnlyDictionary<string, int> Records => Snapshot(_records);

    public IReadOnlyDictionary<string, (long Input, long Output)> Tokens => Snapshot(_tokens);

    public IReadOnlyDictionary<string, int> CacheHits => Snapshot(_cacheHits);

    public IReadOnlyList<string> IncompleteReasons
    {
        get
        {
            lock (_lock)
            {
                return [.. _incomplete];
            }
        }
    }

    public bool Complete => IncompleteReasons.Count == 0;

    /// <summary>The read tools the turn called, in order, with how each ended.</summary>
    public IReadOnlyList<(string Name, string Outcome)> Tools
    {
        get
        {
            lock (_lock)
            {
                return [.. _tools];
            }
        }
    }

    /// <summary>The investigation's model attempts, in order.</summary>
    public IReadOnlyList<AttemptRecord> Attempts
    {
        get
        {
            lock (_lock)
            {
                return [.. _attempts];
            }
        }
    }

    /// <summary><c>none</c>, <c>escalated</c>, or <c>refused:</c> and the bound that refused it; null before an attempt ends.</summary>
    public string? Escalation
    {
        get
        {
            lock (_lock)
            {
                return _escalation;
            }
        }
    }

    public static void RecordEscalation(string escalation)
    {
        if (Current is { } usage)
        {
            lock (usage._lock)
            {
                usage._escalation = escalation;
            }
        }
    }

    public static void RecordAttempt(AttemptRecord attempt)
    {
        if (Current is { } usage)
        {
            lock (usage._lock)
            {
                usage._attempts.Add(attempt);
            }
        }
    }

    /// <summary>Screen verdicts by kind, such as <c>question:clean</c> or <c>tool_text:attack</c>.</summary>
    public IReadOnlyDictionary<string, int> Screens => Snapshot(_screens);

    public static void RecordTool(string name, string outcome)
    {
        if (Current is { } usage)
        {
            lock (usage._lock)
            {
                usage._tools.Add((name, outcome));
            }
        }
    }

    /// <summary>Screen verdicts of one kind: the turn's count, and the block counter for anything not clean.</summary>
    public static void RecordScreen(string kind, string verdict, int count = 1)
    {
        if (verdict != "clean")
        {
            AgentTelemetry.ScreenBlocks.Add(count, new("kind", kind), new("verdict", verdict));
        }

        if (Current is { } usage && count > 0)
        {
            usage.Count(usage._screens, $"{kind}:{verdict}", count);
        }
    }

    public static void RecordHttpRequest(string service, string? deployment = null)
    {
        AgentTelemetry.HttpRequests.Add(1, new("service", service), new("deployment", deployment));
        if (Current is { } usage)
        {
            usage.Count(usage._httpRequests, service, 1);
            if (deployment is not null)
            {
                usage.Count(usage._modelHttp, deployment, 1);
            }
        }
    }

    /// <summary>One logical model call: one request the agent made, however many HTTP attempts it took.</summary>
    public static void RecordModelCall(string deployment)
    {
        if (Current is { } usage)
        {
            usage.Count(usage._modelCalls, deployment, 1);
        }
    }

    /// <summary>The routing mode the investigation ran under.</summary>
    public static void RecordRouting(string routing)
    {
        if (Current is { } usage)
        {
            lock (usage._lock)
            {
                usage._routing = routing;
            }
        }
    }

    /// <summary>A call whose measured input exceeded its estimate before it was sent.</summary>
    public static void RecordEstimateExceeded()
    {
        if (Current is { } usage)
        {
            lock (usage._lock)
            {
                usage._estimateExceeded++;
            }
        }
    }

    public static void RecordContentSafety(string service, int records)
    {
        AgentTelemetry.ContentSafetyRecords.Add(records, new KeyValuePair<string, object?>("service", service));
        Current?.AddRecords(service, records);
    }

    /// <param name="cachedInput">The part of <paramref name="input"/> served from the prompt cache, billed at its own rate.</param>
    public static void RecordTokens(string deployment, long input, long output, long cachedInput = 0)
    {
        AgentTelemetry.ModelTokens.Add(input, new("direction", "input"), new("deployment", deployment));
        AgentTelemetry.ModelTokens.Add(output, new("direction", "output"), new("deployment", deployment));
        if (Current is { } usage)
        {
            usage.AddTokens(deployment, input, output);
            lock (usage._lock)
            {
                usage._cachedInput[deployment] = usage._cachedInput.GetValueOrDefault(deployment) + cachedInput;
            }
        }
    }

    public static void RecordCacheHit(string cache, int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        AgentTelemetry.CacheHits.Add(count, new KeyValuePair<string, object?>("cache", cache));

        // Screens run inside their screening span, so the hit is attributed to the span it served.
        if (Activity.Current is { } span && span.Source == AgentTelemetry.Source)
        {
            var tag = $"paymentops.cache.{cache}.hits";
            span.SetTag(tag, (span.GetTagItem(tag) as int? ?? 0) + count);
        }

        if (Current is { } usage)
        {
            usage.Count(usage._cacheHits, cache, count);
        }
    }

    public static void RecordIncomplete(string reason) => Current?.MarkIncomplete(reason);

    public void AddRecords(string service, int records) => Count(_records, service, records);

    public void AddTokens(string deployment, long input, long output)
    {
        lock (_lock)
        {
            _tokens[deployment] = _tokens.TryGetValue(deployment, out var t) ? (t.Input + input, t.Output + output) : (input, output);
        }
    }

    public void MarkIncomplete(string reason)
    {
        lock (_lock)
        {
            _incomplete.Add(reason);
        }
    }

    /// <summary>What the investigation used, frozen: later recording doesn't change it.</summary>
    public UsageSnapshot Snapshot()
    {
        lock (_lock)
        {
            var deployments = _tokens.Keys.Concat(_modelCalls.Keys).Concat(_modelHttp.Keys).Distinct(StringComparer.Ordinal)
                .ToImmutableSortedDictionary(
                    d => d,
                    d => new DeploymentUsage(
                        _modelCalls.GetValueOrDefault(d),
                        _modelHttp.GetValueOrDefault(d),
                        _tokens.GetValueOrDefault(d).Input,
                        _cachedInput.GetValueOrDefault(d),
                        _tokens.GetValueOrDefault(d).Output),
                    StringComparer.Ordinal);
            return new UsageSnapshot(
                _routing,
                deployments,
                _records.ToImmutableSortedDictionary(StringComparer.Ordinal),
                _httpRequests.ToImmutableSortedDictionary(StringComparer.Ordinal),
                _cacheHits.ToImmutableSortedDictionary(StringComparer.Ordinal),
                _screens.ToImmutableSortedDictionary(StringComparer.Ordinal),
                [.. _tools],
                [.. _attempts],
                _escalation,
                _estimateExceeded,
                [.. _incomplete]);
        }
    }

    private void Count(Dictionary<string, int> counts, string key, int by)
    {
        lock (_lock)
        {
            counts[key] = counts.GetValueOrDefault(key) + by;
        }
    }

    private Dictionary<string, T> Snapshot<T>(Dictionary<string, T> source)
    {
        lock (_lock)
        {
            return new Dictionary<string, T>(source, StringComparer.Ordinal);
        }
    }
}
