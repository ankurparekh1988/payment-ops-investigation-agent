using System.Text.Json.Nodes;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Evaluation.Tests;

public sealed class InvestigationDatasetTests
{
    private static readonly IReadOnlyList<KnowledgeSearchDocument> Corpus = EvaluationFakes.Corpus;

    // A section whose document and path name it alone, so a readable reference resolves to exactly one key.
    private static readonly KnowledgeSearchDocument General = Unique("general");
    private static readonly KnowledgeSearchDocument Restricted = Unique("restricted");

    private static KnowledgeSearchDocument Unique(string classification) =>
        Corpus.Where(s => s.Classification == classification)
            .GroupBy(s => (s.DocId, s.SectionPath))
            .First(g => g.Count() == 1 && g.Single().Content.Length > 40)
            .Single();

    private static JsonObject Section(KnowledgeSearchDocument s) => new() { ["docId"] = s.DocId, ["sectionPath"] = s.SectionPath };

    private static JsonObject Case(string id = "c1", Action<JsonObject>? change = null)
    {
        var c = new JsonObject
        {
            ["id"] = id,
            ["group"] = "core",
            ["access"] = "general",
            ["role"] = "reader",
            ["question"] = $"Why was the payout for M-1042 late ({id})?",
            ["allowedOutcomes"] = new JsonArray("assessed", "escalated"),
            ["requiredTools"] = new JsonArray("get_merchant_profile", "get_settlement_batches"),
            ["toolArguments"] = new JsonArray(new JsonObject
            {
                ["tool"] = "get_settlement_batches",
                ["constraints"] = new JsonArray(
                    new JsonObject { ["field"] = "merchant_id", ["kind"] = "equals", ["value"] = "M-1042" },
                    new JsonObject { ["field"] = "date_range", ["kind"] = "covers", ["value"] = "2026-11-09" }),
            }),
            ["scoreEvidence"] = true,
            ["evidenceOutcomes"] = null,
            ["expectedEvidence"] = new JsonArray(new JsonArray("incident:INC-FAB-1109", "fact:missing-file:fabrikam-pay:2026-11-09")),
            ["requiredSections"] = new JsonArray(Section(General)),
            ["acceptableSections"] = new JsonArray(Section(General)),
            ["proposal"] = "either",
            ["restrictedMarkers"] = new JsonArray(),
            ["expectedRootCause"] = "Fabrikam's settlement file for 9 November never arrived.",
        };
        change?.Invoke(c);
        return c;
    }

    private static string File(params JsonObject[] cases)
    {
        var path = Path.GetTempFileName();
        System.IO.File.WriteAllText(path, new JsonObject
        {
            ["schemaVersion"] = 2,
            ["description"] = "test",
            ["cases"] = new JsonArray([.. cases]),
        }.ToJsonString());
        return path;
    }

    private static LoadedCases Load(params string[] paths) => InvestigationDataset.Load(paths, Corpus, ["What does the RF-37 review cover?"]);

    private static void Rejects(string path, params string[] problems)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Load(path));
        Assert.All(problems, p => Assert.Contains(p, ex.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_valid_case_loads_with_its_sections_resolved()
    {
        var loaded = Load(File(Case()));

        var c = Assert.Single(loaded.Cases);
        Assert.Equal(General.Id, loaded.SectionKeys[c.RequiredSections[0]]);
        Assert.Equal("restricted", loaded.Classification[Restricted.Id]);
    }

    [Fact]
    public void A_case_with_an_unknown_field_is_rejected() =>
        Rejects(File(Case(change: c => c["expectProposal"] = true)), "expectProposal");

    [Fact]
    public void Unknown_tools_outcomes_proposal_or_group_are_rejected() =>
        Rejects(
            File(Case(change: c =>
            {
                c["requiredTools"] = new JsonArray("get_everything");
                c["toolArguments"] = new JsonArray();
                c["allowedOutcomes"] = new JsonArray("solved");
                c["proposal"] = "maybe";
                c["group"] = "misc";
            })),
            "get_everything",
            "solved",
            "maybe",
            "misc");

    [Fact]
    public void An_evidence_id_with_an_unknown_prefix_is_rejected() =>
        Rejects(File(Case(change: c => c["expectedEvidence"] = new JsonArray(new JsonArray("ticket:INC-1")))), "ticket:INC-1");

    [Fact]
    public void A_section_reference_matching_zero_or_several_sections_is_rejected()
    {
        var missing = new JsonObject { ["docId"] = General.DocId, ["sectionPath"] = "No such heading" };
        Rejects(File(Case(change: c => c["acceptableSections"] = new JsonArray(Section(General), missing))), "No such heading");
    }

    [Fact]
    public void Required_sections_outside_acceptable_sections_are_rejected() =>
        Rejects(File(Case(change: c => c["acceptableSections"] = new JsonArray(Section(Restricted)))), "not in acceptableSections");

    [Fact]
    public void A_restricted_marker_found_in_a_general_section_is_rejected()
    {
        var marker = General.Content[5..30].ToUpperInvariant();
        Rejects(File(Case(change: c => c["restrictedMarkers"] = new JsonArray(marker))), marker);
    }

    [Fact]
    public void An_authorization_case_with_general_access_needs_restricted_markers() =>
        Rejects(File(Case(change: c => c["group"] = "authorization")), "restrictedMarkers");

    [Fact]
    public void Evidence_scored_cases_need_expected_evidence_required_sections_and_a_root_cause() =>
        Rejects(
            File(Case(change: c =>
            {
                c["expectedEvidence"] = new JsonArray();
                c["requiredSections"] = new JsonArray();
                c["expectedRootCause"] = null;
            })),
            "expectedEvidence",
            "requiredSections",
            "expectedRootCause");

    [Fact]
    public void A_knowledge_only_case_may_score_sections_without_evidence_groups()
    {
        var loaded = Load(File(Case(change: c =>
        {
            c["group"] = "knowledge-only";
            c["expectedEvidence"] = new JsonArray();
        })));

        Assert.Empty(Assert.Single(loaded.Cases).ExpectedEvidence);
    }

    [Fact]
    public void Evidence_outcomes_default_to_assessed_and_escalated()
    {
        var c = Assert.Single(Load(File(Case())).Cases);

        Assert.Equal(["assessed", "escalated"], c.CreditableOutcomes);
    }

    [Fact]
    public void Tool_argument_constraints_must_name_a_required_tool_and_a_known_kind() =>
        Rejects(
            File(Case(change: c => c["toolArguments"] = new JsonArray(new JsonObject
            {
                ["tool"] = "get_error_events",
                ["constraints"] = new JsonArray(
                    new JsonObject { ["field"] = "processor_id", ["kind"] = "like", ["value"] = "x" },
                    new JsonObject { ["field"] = "start_date", ["kind"] = "covers", ["value"] = "2026-11-09" }),
            }))),
            "get_error_events",
            "like",
            "'start_date'");

    [Fact]
    public void Duplicate_ids_across_files_are_rejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Load(File(Case("same")), File(Case("same", c => c["question"] = "Another question?"))));

        Assert.Contains("same", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_question_repeating_a_retrieval_case_is_rejected() =>
        Rejects(File(Case(change: c => c["question"] = "  what does the   RF-37 review cover? ")), "repeats a retrieval case");

    [Fact]
    public void All_problems_are_reported_together() =>
        Rejects(
            File(Case("first", c => c["proposal"] = "maybe"), Case("second", c => c["role"] = "admin")),
            "first",
            "second");
}
