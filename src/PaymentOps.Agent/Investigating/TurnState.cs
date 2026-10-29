using PaymentOps.Agent.Contracts;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Everything one investigation established, written only by tool and guard code from typed results, never
/// from model text: knowledge sections numbered S1…Sn, records and derived facts by evidence id, the gaps
/// observed, and the one accepted assessment. An assessment is checked against this and nothing else.
/// </summary>
internal sealed class TurnState
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, RetrievedSection> _sectionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idsBySectionKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _withheldSections = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sectionsReadInFull = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EvidenceItem> _evidence = new(StringComparer.Ordinal);
    private readonly List<ObservedGap> _gaps = [];
    private bool _structuredAnswered;
    private bool _structuredFailed;

    public bool Stopped { get; private set; }

    /// <summary>The validated submission; once set, nothing the model asks for runs.</summary>
    public AssessmentSubmission? Accepted { get; private set; }

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
                return _structuredFailed && !_structuredAnswered;
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
            return [.. _evidence.Values.OfType<DerivedEvidence>().Where(fact => EvidenceIds.IsEventGroup(fact.Id) && fact.SourceIds.Contains(eventId, StringComparer.Ordinal))];
        }
    }

    /// <summary>Processors with a missing file this turn: more than one points to a platform incident, not a processor's.</summary>
    public IReadOnlySet<string> ProcessorsWithMissingFiles
    {
        get
        {
            lock (_lock)
            {
                return _evidence.Values.OfType<DerivedEvidence>()
                    .Where(fact => fact.Id.StartsWith(EvidenceIds.MissingFilePrefix, StringComparison.Ordinal))
                    .SelectMany(fact => fact.SourceIds)
                    .Where(id => id.StartsWith(EvidenceIds.ProcessorPrefix, StringComparison.Ordinal))
                    .Select(id => id[EvidenceIds.ProcessorPrefix.Length..])
                    .ToHashSet(StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// Records whether the model read all of a section. One it never read in full keeps its citation id for the
    /// Source page but can't support anything; once read in full, it can.
    /// </summary>
    public void SectionScreened(string id, bool readInFull)
    {
        lock (_lock)
        {
            if (readInFull)
            {
                _sectionsReadInFull.Add(id);
                _withheldSections.Remove(id);
            }
            else if (!_sectionsReadInFull.Contains(id))
            {
                _withheldSections.Add(id);
            }
        }
    }

    public bool IsSupportEligible(string id)
    {
        lock (_lock)
        {
            return !_withheldSections.Contains(id);
        }
    }

    public bool TryGetSection(string id, out RetrievedSection section)
    {
        lock (_lock)
        {
            return _sectionsById.TryGetValue(id, out section!);
        }
    }

    /// <summary>How a registered section is shown: from the server's copy, never from what the model wrote.</summary>
    public Citation CitationFor(string id)
    {
        lock (_lock)
        {
            var section = _sectionsById[id];
            return new Citation(id, section.Key, section.Title, section.SectionPath);
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

    /// <summary>Stops the turn; true only the first time, so the stop is reported once however it was reached.</summary>
    public bool Stop()
    {
        if (Stopped)
        {
            return false;
        }

        Stopped = true;
        AddGap(ObservedGap.LimitReached);
        return true;
    }

    public void Accept(AssessmentSubmission submission) => Accepted = submission;

    public void StructuredOutcome(bool answered)
    {
        lock (_lock)
        {
            _structuredAnswered |= answered;
            _structuredFailed |= !answered;
        }
    }
}
