using PaymentOps.Domain.Processors;

namespace PaymentOps.Tools;

/// <summary>
/// What one tool call builds up: every record it read and each fact it computed, by evidence id, and the lists
/// it cut short. Facts are computed over everything read; only what the model is shown is capped.
/// </summary>
public sealed class EvidenceCollector(int maxListItems)
{
    private readonly Dictionary<string, EvidenceItem> _evidence = new(StringComparer.Ordinal);
    private readonly List<Truncation> _truncations = [];

    // The data is deterministic, so an id seen twice is the same record or fact; the first stays.
    public string Record(string id, string label)
    {
        _evidence.TryAdd(id, new RecordEvidence(id, label));
        return id;
    }

    public string Incident(ProcessorIncident incident)
    {
        var id = EvidenceIds.Incident(incident.Id);
        _evidence.TryAdd(id, new IncidentEvidence(id, $"Incident {incident.Id}", incident.ProcessorId, incident.ProcessorReference));
        return id;
    }

    public string Derive(string id, string label, IReadOnlyList<string> sourceIds)
    {
        _evidence.TryAdd(id, new DerivedEvidence(id, label, sourceIds));
        return id;
    }

    public ListOf<T> Cap<T>(IReadOnlyList<T> items, string what)
    {
        var list = ListOf.Capped(items, maxListItems, what, out var truncation);
        if (truncation is not null)
        {
            _truncations.Add(truncation);
        }

        return list;
    }

    public ToolResult.Ok Ok(object body) => new(body, [.. _evidence.Values], [.. _truncations]);
}
