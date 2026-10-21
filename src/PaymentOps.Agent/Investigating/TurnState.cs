using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Everything one investigation established, written only by tool and guard code from typed results, never
/// from model text: knowledge sections numbered S1…Sn, records and derived facts by evidence id, and the gaps
/// observed. An assessment is checked against this and nothing else.
/// </summary>
internal sealed class TurnState
{
    private const string EventGroupPrefix = EvidenceIds.FactPrefix + "event-group:";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, RetrievedSection> _sectionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idsBySectionKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EvidenceItem> _evidence = new(StringComparer.Ordinal);
    private readonly List<ObservedGap> _gaps = [];
    private readonly HashSet<string> _structuredToolsSucceeded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _structuredToolsFailed = new(StringComparer.Ordinal);

    public bool Stopped { get; private set; }

    /// <summary>An assessment was accepted; nothing the model asks for after that runs.</summary>
    public bool Finished { get; private set; }

    public IReadOnlyList<ObservedGap> Gaps
    {
        get
        {
            lock (_lock)
            {
                return [.. _gaps];
            }
        }
    }

    /// <summary>A structured data tool was tried and none answered: the server then forces an incomplete outcome.</summary>
    public bool EveryStructuredSourceFailed
    {
        get
        {
            lock (_lock)
            {
                return _structuredToolsFailed.Count > 0 && _structuredToolsSucceeded.Count == 0;
            }
        }
    }

    public bool HasKnowledge
    {
        get
        {
            lock (_lock)
            {
                return _sectionsById.Count > 0;
            }
        }
    }

    // A section already registered keeps its id, so the same text never has two citations.
    public string RegisterSection(RetrievedSection section)
    {
        lock (_lock)
        {
            if (!_idsBySectionKey.TryGetValue(section.Key, out var id))
            {
                id = $"S{_sectionsById.Count + 1}";
                _idsBySectionKey[section.Key] = id;
                _sectionsById[id] = section;
            }

            return id;
        }
    }

    // The data is deterministic, so a fact registered twice is the same fact; the first stays.
    public void Register(IEnumerable<EvidenceItem> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
            {
                _evidence.TryAdd(item.Id, item);
            }
        }
    }

    public bool TryGetEvidence(string id, out EvidenceItem item)
    {
        lock (_lock)
        {
            return _evidence.TryGetValue(id, out item!);
        }
    }

    /// <summary>The event aggregates that counted this event, so a count can be checked against its aggregate.</summary>
    public IReadOnlyList<DerivedEvidence> EventGroupsContaining(string eventId)
    {
        lock (_lock)
        {
            return [.. _evidence.Values.OfType<DerivedEvidence>().Where(fact =>
                fact.Id.StartsWith(EventGroupPrefix, StringComparison.Ordinal) && fact.SourceIds.Contains(eventId, StringComparer.Ordinal))];
        }
    }

    public bool TryGetSection(string id, out RetrievedSection section)
    {
        lock (_lock)
        {
            return _sectionsById.TryGetValue(id, out section!);
        }
    }

    public void AddGap(ObservedGap gap)
    {
        lock (_lock)
        {
            if (!_gaps.Contains(gap))
            {
                _gaps.Add(gap);
            }
        }
    }

    public void Stop(string tool)
    {
        lock (_lock)
        {
            Stopped = true;
        }

        AddGap(ObservedGap.LimitReached(tool));
    }

    public void Finish()
    {
        lock (_lock)
        {
            Finished = true;
        }
    }

    public void StructuredOutcome(string tool, bool answered)
    {
        lock (_lock)
        {
            (answered ? _structuredToolsSucceeded : _structuredToolsFailed).Add(tool);
        }
    }
}
