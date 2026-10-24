using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Cases;

/// <param name="SectionKeys">Every readable section reference in the cases, resolved against the corpus.</param>
/// <param name="Classification">Section key to <c>general</c> or <c>restricted</c>, for every section in the corpus.</param>
internal sealed record LoadedCases(
    IReadOnlyList<InvestigationCase> Cases,
    IReadOnlyDictionary<ExpectedSection, string> SectionKeys,
    IReadOnlyDictionary<string, string> Classification)
{
    public IReadOnlySet<string> Keys(IEnumerable<ExpectedSection> sections) => sections.Select(s => SectionKeys[s]).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Loads the investigation cases strictly and checks every label against the tools, the evidence-id spellings and
/// the corpus, reporting all problems at once so a reviewer fixes a file in one pass.
/// </summary>
internal static partial class InvestigationDataset
{
    public const int SupportedSchemaVersion = 2;

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    /// <param name="retrievalQuestions">Calibration and retrieval questions, which an investigation case must not repeat.</param>
    public static LoadedCases Load(IReadOnlyList<string> paths, IReadOnlyList<KnowledgeSearchDocument> corpus, IReadOnlyList<string> retrievalQuestions)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(corpus);

        var cases = paths.SelectMany(Read).ToList();
        var docs = corpus.Select(s => s.DocId).ToHashSet(StringComparer.Ordinal);
        var general = corpus.Where(s => s.Classification == KnowledgeClassification.General).Select(s => s.Content).ToList();
        var retrieval = retrievalQuestions.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var keys = new Dictionary<ExpectedSection, string>();
        var problems = new List<string>();

        problems.AddRange(cases.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"{g.Key}: id appears more than once."));
        foreach (var c in cases)
        {
            foreach (var problem in Check(c, general, retrieval))
            {
                problems.Add($"{c.Id}: {problem}");
            }

            foreach (var section in c.RequiredSections.Concat(c.AcceptableSections).Distinct())
            {
                if (CalibrationReferences.TryResolve(section, corpus, docs, out var key, out var error))
                {
                    keys[section] = key;
                }
                else
                {
                    problems.Add($"{c.Id}: {error}");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, problems));
        }

        return new LoadedCases(cases, keys, corpus.ToDictionary(s => s.Id, s => s.Classification, StringComparer.Ordinal));
    }

    private static IReadOnlyList<InvestigationCase> Read(string path)
    {
        CaseFile file;
        try
        {
            file = JsonSerializer.Deserialize<CaseFile>(File.ReadAllBytes(path), Strict) ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }

        return file.SchemaVersion == SupportedSchemaVersion ? file.Cases
            : throw new InvalidDataException($"{path} is schema version {file.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
    }

    private static IEnumerable<string> Check(InvestigationCase c, List<string> generalText, HashSet<string> retrievalQuestions)
    {
        if (!InvestigationCase.Groups.Contains(c.Group))
        {
            yield return $"group '{c.Group}' must be one of {string.Join(", ", InvestigationCase.Groups)}.";
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
        else if (retrievalQuestions.Contains(Normalize(c.Question)))
        {
            yield return "question repeats a retrieval case.";
        }

        if (c.AllowedOutcomes.Count == 0)
        {
            yield return "allowedOutcomes is empty.";
        }

        foreach (var outcome in c.AllowedOutcomes.Concat(c.EvidenceOutcomes ?? []).Where(o => !InvestigationCase.AllOutcomes.Contains(o)))
        {
            yield return $"unknown outcome '{outcome}'.";
        }

        foreach (var tool in c.RequiredTools.Where(t => !ToolNames.ReadTools.Contains(t)))
        {
            yield return $"unknown tool '{tool}'.";
        }

        foreach (var label in c.ToolArguments)
        {
            if (!c.RequiredTools.Contains(label.Tool))
            {
                yield return $"toolArguments names '{label.Tool}', which isn't a required tool.";
            }

            foreach (var constraint in label.Constraints)
            {
                if (constraint.Kind is not (ArgumentConstraint.EqualsKind or ArgumentConstraint.CoversKind))
                {
                    yield return $"constraint kind '{constraint.Kind}' must be equals or covers.";
                }
                else if (constraint.Kind == ArgumentConstraint.CoversKind && (constraint.Field != ArgumentConstraint.DateRangeField || !ToolArguments.TryDate(constraint.Value, out _)))
                {
                    yield return $"covers needs field {ArgumentConstraint.DateRangeField} and a YYYY-MM-DD date, not '{constraint.Field}' and '{constraint.Value}'.";
                }
            }
        }

        foreach (var id in c.ExpectedEvidence.SelectMany(g => g).Where(id => !EvidenceIds.Prefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal))))
        {
            yield return $"evidence id '{id}' has an unknown prefix.";
        }

        if (c.ExpectedEvidence.Any(g => g.Count == 0))
        {
            yield return "expectedEvidence has an empty group.";
        }

        foreach (var section in c.RequiredSections.Where(s => !c.AcceptableSections.Contains(s)))
        {
            yield return $"required section '{section.DocId}: {section.SectionPath}' is not in acceptableSections.";
        }

        if (c.Proposal is not (InvestigationCase.ProposalExpected or InvestigationCase.ProposalForbidden or InvestigationCase.ProposalEither))
        {
            yield return $"proposal '{c.Proposal}' must be expected, forbidden or either.";
        }

        if (c.ScoreEvidence)
        {
            // A procedure question is answered from the runbooks alone, so it has sections to cite but no records.
            if (c.ExpectedEvidence.Count == 0 && c.Group != InvestigationCase.KnowledgeOnlyGroup)
            {
                yield return "an evidence-scored case needs expectedEvidence.";
            }

            if (c.RequiredSections.Count == 0)
            {
                yield return "an evidence-scored case needs requiredSections.";
            }

            if (string.IsNullOrWhiteSpace(c.ExpectedRootCause))
            {
                yield return "an evidence-scored case needs expectedRootCause.";
            }
        }

        if (c.Group == InvestigationCase.AuthorizationGroup && c.Access == KnowledgeClassification.General && c.RestrictedMarkers.Count == 0)
        {
            yield return "a general authorization case needs restrictedMarkers.";
        }

        foreach (var marker in c.RestrictedMarkers)
        {
            if (string.IsNullOrWhiteSpace(marker))
            {
                yield return "a restricted marker is empty.";
            }
            else if (generalText.Any(text => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"restricted marker '{marker}' occurs in a General section.";
            }
        }
    }

    // Trimmed, whitespace collapsed and case ignored, so a reworded copy of the same question still matches.
    private static string Normalize(string question) => Whitespace().Replace(question.Trim(), " ").ToLower(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    private sealed record CaseFile(int SchemaVersion, string Description, IReadOnlyList<InvestigationCase> Cases);
}
