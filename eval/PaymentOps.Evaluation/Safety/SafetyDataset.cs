using System.Text.Json;

using PaymentOps.Agent.Contracts;
using PaymentOps.Data.Demo;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Challenger;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Evaluation.Safety;

/// <param name="PoisonedSections">Case id to the section key its knowledge poison is appended to.</param>
internal sealed record LoadedSafetyCases(IReadOnlyList<SafetyCase> Cases, IReadOnlyDictionary<string, string> PoisonedSections)
{
    public IReadOnlyList<SafetyCase> OfKind(string kind) => [.. Cases.Where(c => c.Kind == kind)];
}

/// <summary>
/// Loads the safety cases strictly, reporting every problem at once: each kind carries exactly the parts it
/// needs, and every poison target exists in the demo data or the corpus.
/// </summary>
internal static class SafetyDataset
{
    public const int SupportedSchemaVersion = 1;

    private static readonly IReadOnlyList<string> ConfidenceWords = [.. Enum.GetValues<Confidence>().Select(c => Outcomes.Word(c))];

    public static LoadedSafetyCases Load(string path, IReadOnlyList<KnowledgeSearchDocument> corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var file = StrictFiles.Read<SafetyFile>(path, StrictFiles.CamelCase);
        if (file.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException($"{path} is schema version {file.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
        }

        var processors = KnownProcessor.FromDemoData().Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var withIncidents = ProcessorsWithIncidents();
        var docs = corpus.Select(s => s.DocId).ToHashSet(StringComparer.Ordinal);
        var general = corpus.Where(s => s.Classification == KnowledgeClassification.General).Select(s => s.Content).ToList();
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = file.Cases.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"{g.Key}: id appears more than once.").ToList();
        foreach (var c in file.Cases)
        {
            problems.AddRange(Check(c, processors, withIncidents, general).Select(problem => $"{c.Id}: {problem}"));
            if (c.Poison is { Surface: SafetyPoison.Knowledge } poison)
            {
                if (!CalibrationReferences.TryResolve(new ExpectedSection(poison.Target, poison.SectionPath ?? string.Empty), corpus, docs, out var key, out var error))
                {
                    problems.Add($"{c.Id}: {error}");
                }
                else if (corpus.First(s => s.Id == key).Classification != KnowledgeClassification.General)
                {
                    problems.Add($"{c.Id}: knowledge poison must target a General section.");
                }
                else
                {
                    sections[c.Id] = key;
                }
            }
        }

        return problems.Count > 0 ? throw new InvalidDataException(string.Join(Environment.NewLine, problems)) : new LoadedSafetyCases(file.Cases, sections);
    }

    private static IEnumerable<string> Check(SafetyCase c, HashSet<string> processors, HashSet<string> withIncidents, List<string> generalText)
    {
        if (!SafetyCase.Kinds.Contains(c.Kind))
        {
            yield return $"kind '{c.Kind}' must be one of {string.Join(", ", SafetyCase.Kinds)}.";
        }

        if (c.Access is not (KnowledgeClassification.General or KnowledgeClassification.Restricted))
        {
            yield return "access must be general or restricted.";
        }

        if (c.Role is not (InvestigationCase.Reader or InvestigationCase.Engineer))
        {
            yield return $"role '{c.Role}' must be reader or engineer.";
        }

        if (string.IsNullOrWhiteSpace(c.Question))
        {
            yield return "question is empty.";
        }

        var poisoned = c.Kind is SafetyCase.IndirectAttack or SafetyCase.BenignText;
        if (poisoned != c.Poison is not null)
        {
            yield return poisoned ? "this kind needs a poison." : "only indirect-attack and benign-text cases carry a poison.";
        }

        if ((c.Kind == SafetyCase.IndirectAttack) != c.Forbidden is not null)
        {
            yield return c.Kind == SafetyCase.IndirectAttack ? "an indirect attack needs forbidden effects." : "only indirect attacks list forbidden effects.";
        }

        if (c.Poison is { } poison)
        {
            if (string.IsNullOrWhiteSpace(poison.Payload))
            {
                yield return "the poison's payload is empty.";
            }

            switch (poison.Surface)
            {
                case SafetyPoison.ProcessorIncident when !processors.Contains(poison.Target):
                    yield return $"no processor '{poison.Target}'.";
                    break;
                case SafetyPoison.ProcessorIncident when !withIncidents.Contains(poison.Target):
                    yield return $"processor '{poison.Target}' has no incidents to plant text in.";
                    break;
                case SafetyPoison.ProcessorIncident when poison.SectionPath is not null:
                    yield return "an incident poison has no sectionPath.";
                    break;
                case SafetyPoison.ProcessorIncident or SafetyPoison.Knowledge:
                    break;
                default:
                    yield return $"surface '{poison.Surface}' must be {SafetyPoison.ProcessorIncident} or {SafetyPoison.Knowledge}.";
                    break;
            }
        }

        if (c.Forbidden is { } forbidden)
        {
            if (!forbidden.Any)
            {
                yield return "forbidden effects list nothing.";
            }

            foreach (var outcome in forbidden.Outcomes.Where(o => !InvestigationCase.AllOutcomes.Contains(o)))
            {
                yield return $"unknown outcome '{outcome}'.";
            }

            foreach (var level in forbidden.Confidence.Where(l => !ConfidenceWords.Contains(l)))
            {
                yield return $"unknown confidence '{level}'.";
            }

            if (forbidden.Text.Any(string.IsNullOrWhiteSpace))
            {
                yield return "a forbidden phrase is empty.";
            }

            // Restricted phrases must be ones a General section can't supply, or a General answer would trip them honestly.
            foreach (var phrase in forbidden.RestrictedSections ? forbidden.Text : [])
            {
                if (generalText.Any(text => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return $"restricted phrase '{phrase}' occurs in a General section.";
                }
            }

            if (forbidden.RestrictedSections && c.Access != KnowledgeClassification.General)
            {
                yield return "restrictedSections only applies under general access.";
            }
        }
    }

    // Text is appended to a processor's incidents, so a processor with none would carry the poison nowhere.
    private static HashSet<string> ProcessorsWithIncidents()
    {
        using var stream = typeof(DemoDataServiceCollectionExtensions).Assembly.GetManifestResourceStream("data/datasets/default/dataset.json")
            ?? throw new InvalidOperationException("The demo dataset isn't embedded.");
        using var json = JsonDocument.Parse(stream);
        return [.. json.RootElement.GetProperty("incidents").EnumerateArray().Select(i => i.GetProperty("processorId").GetString()!)];
    }

    private sealed record SafetyFile(int SchemaVersion, string Description, IReadOnlyList<SafetyCase> Cases);
}
