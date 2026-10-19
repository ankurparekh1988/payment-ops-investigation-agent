using System.Text.Json;
using System.Text.Json.Serialization;

using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Evaluation.Retrieval;

/// <summary>
/// The retrieval calibration cases. They choose the retrieval mode and any semantic floor, so they
/// aren't independent evidence of how well retrieval generalizes; held-out cases come later.
/// </summary>
internal sealed record CalibrationDataset(int SchemaVersion, IReadOnlyList<CalibrationCase> Cases)
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static CalibrationDataset Load(string path)
    {
        var dataset = JsonSerializer.Deserialize<CalibrationDataset>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        if (dataset.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException($"{path} is schema version {dataset.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
        }

        var duplicate = dataset.Cases.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? dataset : throw new InvalidDataException($"Case id '{duplicate.Key}' is used more than once.");
    }
}

internal sealed record CalibrationCase(string Id, CaseKind Kind, string Access, string Question, IReadOnlyList<ExpectedSection> Expected)
{
    public bool IsAnswerable => Kind is CaseKind.Critical or CaseKind.Paraphrase;
}

internal sealed record ExpectedSection(string DocId, string SectionPath);

internal enum CaseKind
{
    Critical,
    Paraphrase,
    Adjacent,
    Unrelated,
    Probe,
}

/// <summary>Turns readable references into section keys using the corpus's own sections, so the dataset never stores keys.</summary>
internal static class CalibrationReferences
{
    public static IReadOnlyDictionary<ExpectedSection, string> Resolve(CalibrationDataset dataset, IReadOnlyList<KnowledgeSearchDocument> sections)
    {
        var docs = sections.Select(s => s.DocId).ToHashSet(StringComparer.Ordinal);
        var keys = new Dictionary<ExpectedSection, string>();
        var errors = new List<string>();
        foreach (var testCase in dataset.Cases)
        {
            if (testCase.Access is not ("general" or "restricted"))
            {
                errors.Add($"{testCase.Id}: access must be general or restricted.");
            }

            foreach (var expected in testCase.Expected)
            {
                var matches = sections.Where(s => s.DocId == expected.DocId && s.SectionPath == expected.SectionPath).ToList();
                if (!docs.Contains(expected.DocId))
                {
                    errors.Add($"{testCase.Id}: no document '{expected.DocId}'.");
                }
                else if (matches.Count != 1)
                {
                    errors.Add($"{testCase.Id}: '{expected.SectionPath}' matches {matches.Count} indexed sections; it must match exactly one with text of its own.");
                }
                else
                {
                    keys[expected] = matches[0].Id;
                }
            }
        }

        return errors.Count == 0 ? keys : throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }
}
