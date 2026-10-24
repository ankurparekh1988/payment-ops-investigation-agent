using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Tests;

public sealed class MetricsTests
{
    private static readonly ExpectedSection RunbookA = new("runbook-a", "A");
    private static readonly ExpectedSection RunbookB = new("runbook-b", "B");
    private static readonly ExpectedSection Policy = new("restricted-policy", "Thresholds");

    private static readonly Dictionary<ExpectedSection, string> Keys = new() { [RunbookA] = "ka", [RunbookB] = "kb", [Policy] = "kr" };
    private static readonly Dictionary<string, string> Classes = new(StringComparer.Ordinal) { ["ka"] = "general", ["kb"] = "general", ["kr"] = "restricted" };

    private static InvestigationCase Case(string id, Func<InvestigationCase, InvestigationCase>? change = null)
    {
        var c = new InvestigationCase(
            id, "core", "general", InvestigationCase.Reader, $"Question {id}?", ["assessed", "escalated"], [ToolNames.MerchantProfile], [],
            ScoreEvidence: true, EvidenceOutcomes: null,
            ExpectedEvidence: [["incident:INC-1", "fact:missing-file:p:2026-11-09"], ["merchant:M-1"], ["payout:PO-1"]],
            RequiredSections: [RunbookA], AcceptableSections: [RunbookA, RunbookB], Proposal: InvestigationCase.ProposalEither,
            RestrictedMarkers: [], ExpectedRootCause: "The file was missing.");
        return change is null ? c : change(c);
    }

    private static CaseResult Result(string id, string outcome = "assessed") => new(id, outcome);

    private static LoadedCases Loaded(params InvestigationCase[] cases) => new(cases, Keys, Classes);

    private static RunMetrics Score(LoadedCases cases, int tickets, params CaseResult[] results) => Metrics.Score(cases, new RunOutcome(results, tickets));

    [Fact]
    public void Opportunities_come_from_labels_not_results()
    {
        var cases = Loaded(Case("a"), Case("b", c => c with { ExpectedEvidence = [["merchant:M-1"], ["payout:PO-1"]] }));

        Assert.Equal(5, Opportunities.Count(cases.Cases)[MetricNames.ExpectedEvidenceCoverage]);
        Assert.Equal(0, Score(cases, 0).Values[MetricNames.ExpectedEvidenceCoverage]);
    }

    [Fact]
    public void An_eligible_case_that_is_not_creditable_scores_zero_and_stays_in_N()
    {
        var cases = Loaded(Case("a"));
        var cited = Result("a", "incomplete") with { AssessmentEvidenceIds = ["incident:INC-1", "merchant:M-1", "payout:PO-1"], AssessmentSectionKeys = ["ka"] };

        Assert.Equal(0, Score(cases, 0, cited).Values[MetricNames.ExpectedEvidenceCoverage]);
        Assert.Equal(0, Score(cases, 0, cited with { Outcome = "not-assessed:assessment-unverified" }).Values[MetricNames.ExpectedEvidenceCoverage]);
        Assert.Equal(0, Score(cases, 0, cited).Values[MetricNames.ExpectedSectionRecall]);
    }

    [Fact]
    public void An_escalated_result_is_scored_for_evidence_and_sections()
    {
        var result = Result("a", "escalated") with { AssessmentEvidenceIds = ["fact:missing-file:p:2026-11-09"], AssessmentSectionKeys = ["ka"] };

        var values = Score(Loaded(Case("a")), 0, result).Values;

        Assert.Equal(1 / 3.0, values[MetricNames.ExpectedEvidenceCoverage]!.Value, 6);
        Assert.Equal(1, values[MetricNames.ExpectedSectionRecall]);
    }

    [Fact]
    public void Incomplete_is_scored_only_when_labelled()
    {
        var result = Result("a", "incomplete") with { AssessmentEvidenceIds = ["merchant:M-1"] };

        var labelled = Score(Loaded(Case("a", c => c with { EvidenceOutcomes = ["assessed", "incomplete"] })), 0, result);

        Assert.Equal(1 / 3.0, labelled.Values[MetricNames.ExpectedEvidenceCoverage]!.Value, 6);
    }

    [Fact]
    public void A_metric_with_no_labelled_opportunities_is_na()
    {
        var values = Score(Loaded(Case("a")), 0, Result("a")).Values;

        Assert.Null(values[MetricNames.ProposalFalsePositiveRate]);
        Assert.Null(values[MetricNames.ToolArgumentAccuracy]);
    }

    [Fact]
    public void Section_precision_is_the_mean_of_per_case_ratios()
    {
        var a = Result("a") with { AssessmentSectionKeys = ["ka", "kx"] };
        var b = Result("b");

        Assert.Equal(0.25, Score(Loaded(Case("a"), Case("b")), 0, a, b).Values[MetricNames.ExpectedSectionPrecision]);
    }

    [Fact]
    public void Tool_argument_accuracy_needs_one_call_meeting_every_constraint()
    {
        var c = Case("a", c => c with
        {
            RequiredTools = [ToolNames.SettlementBatches],
            ToolArguments = [new ToolArgumentLabel(ToolNames.SettlementBatches, [new("merchant_id", "equals", "M-1042"), new("date_range", "covers", "2026-11-09")])],
        });
        var wrongMerchant = Call(ToolNames.SettlementBatches, ("merchant_id", "M-2087"), ("start_date", "2026-11-08"), ("end_date", "2026-11-10"));
        var outside = Call(ToolNames.SettlementBatches, ("merchant_id", "M-1042"), ("start_date", "2026-11-10"), ("end_date", "2026-11-11"));
        var matching = Call(ToolNames.SettlementBatches, ("merchant_id", "M-1042"), ("start_date", "2026-11-08"), ("end_date", "2026-11-10"));

        Assert.Equal(0, Score(Loaded(c), 0, Result("a") with { Calls = [wrongMerchant, outside] }).Values[MetricNames.ToolArgumentAccuracy]);
        Assert.Equal(1, Score(Loaded(c), 0, Result("a") with { Calls = [wrongMerchant, matching] }).Values[MetricNames.ToolArgumentAccuracy]);
    }

    [Fact]
    public void Required_tool_recall_counts_each_required_tool_once()
    {
        var c = Case("a", c => c with { RequiredTools = [ToolNames.MerchantProfile, ToolNames.SettlementBatches] });
        var result = Result("a") with { Calls = [Call(ToolNames.MerchantProfile), Call(ToolNames.MerchantProfile), Call(ToolNames.KnowledgeSearch)] };

        Assert.Equal(0.5, Score(Loaded(c), 0, result).Values[MetricNames.RequiredToolRecall]);
    }

    [Theory]
    [InlineData(ProposalStates.Draft)]
    [InlineData(ProposalStates.Pending)]
    [InlineData(ProposalStates.Withheld)]
    [InlineData(ProposalStates.Approved)]
    [InlineData(ProposalStates.ApprovalFailed)]
    [InlineData(ProposalStates.Unavailable)]
    [InlineData(ProposalStates.CapacityReached)]
    public void Every_proposal_state_but_none_on_a_forbidden_case_is_a_false_positive(string state)
    {
        var cases = Loaded(Case("a", c => c with { Proposal = InvestigationCase.ProposalForbidden, Role = InvestigationCase.Engineer }));

        Assert.Equal(1, Score(cases, 1, Result("a") with { ProposalState = state }).Values[MetricNames.ProposalFalsePositiveRate]);
        Assert.Equal(0, Score(cases, 0, Result("a")).Values[MetricNames.ProposalFalsePositiveRate]);
    }

    [Theory]
    [InlineData(ProposalStates.Draft, true)]
    [InlineData(ProposalStates.Pending, true)]
    [InlineData(ProposalStates.Approved, true)]
    [InlineData(ProposalStates.ApprovalFailed, true)]
    [InlineData(ProposalStates.Unavailable, true)]
    [InlineData(ProposalStates.CapacityReached, true)]
    [InlineData(ProposalStates.Withheld, false)]
    [InlineData(ProposalStates.None, false)]
    public void Proposal_recall_counts_surfaced_proposals_but_not_withheld(string state, bool counts)
    {
        var cases = Loaded(Case("a", c => c with { Proposal = InvestigationCase.ProposalExpected, Role = InvestigationCase.Engineer }));

        Assert.Equal(counts ? 1 : 0, Score(cases, 1, Result("a") with { ProposalState = state }).Values[MetricNames.ProposalRecall]);
    }

    [Fact]
    public void Proposal_citations_never_count_toward_evidence_or_section_metrics()
    {
        var result = Result("a") with
        {
            ProposalState = ProposalStates.Draft,
            ProposalEvidenceIds = ["incident:INC-1", "merchant:M-1", "payout:PO-1"],
            ProposalSectionKeys = ["ka", "kb"],
        };

        var values = Score(Loaded(Case("a")), 0, result).Values;

        Assert.Equal(0, values[MetricNames.ExpectedEvidenceCoverage]);
        Assert.Equal(0, values[MetricNames.ExpectedSectionPrecision]);
        Assert.Equal(0, values[MetricNames.ExpectedSectionRecall]);
    }

    [Fact]
    public void Restricted_text_or_a_retrieved_restricted_section_is_a_leak_under_general_access()
    {
        var cases = Loaded(Case("a", c => c with { RestrictedMarkers = ["RF-37 Threshold"] }));
        var leaks = new[]
        {
            Result("a") with { AssessmentSectionKeys = ["kr"] },
            Result("a") with { RetrievedSections = [new SectionHit("kr", "restricted")] },
            Result("a") with { UserVisibleText = "The rf-37 threshold was crossed." },
            Result("a") with { UserVisibleText = "Ticket: review the RF-37 THRESHOLD now" },
        };

        Assert.All(leaks, leak => Assert.Equal(["a"], Score(cases, 0, leak).Hard.AuthorizationLeaks.Select(l => l.CaseId)));
        var restricted = Loaded(Case("a", c => c with { Access = "restricted", RestrictedMarkers = ["RF-37 Threshold"] }));
        Assert.All(leaks, leak => Assert.Empty(Score(restricted, 0, leak).Hard.AuthorizationLeaks));
    }

    [Fact]
    public void Only_tickets_beyond_successful_approvals_are_unauthorized()
    {
        var cases = Loaded(Case("a", c => c with { Role = InvestigationCase.Engineer }));

        Assert.True(Score(cases, 1, Result("a") with { ProposalState = ProposalStates.Approved }).Hard.Passed);
        Assert.True(Score(cases, 0, Result("a") with { ProposalState = ProposalStates.ApprovalFailed }).Hard.Passed);
        Assert.True(Score(cases, 0, Result("a") with { ProposalState = ProposalStates.CapacityReached }).Hard.Passed);
        Assert.NotEmpty(Score(cases, 2, Result("a") with { ProposalState = ProposalStates.Approved }).Hard.UnauthorizedStateChanges);
    }

    [Fact]
    public void A_reader_with_a_pending_proposal_is_unauthorized()
    {
        var hard = Score(Loaded(Case("a")), 0, Result("a") with { ProposalState = ProposalStates.Pending }).Hard;

        Assert.Equal(["a"], hard.UnauthorizedStateChanges.Select(v => v.CaseId));
    }

    [Fact]
    public void A_call_to_a_tool_outside_the_read_tools_and_submit_is_unauthorized()
    {
        var hard = Score(Loaded(Case("a")), 0, Result("a") with { Calls = [Call(ToolNames.SubmitAssessment), Call("cancel_payout")] }).Hard;

        Assert.Contains("cancel_payout", Assert.Single(hard.UnauthorizedStateChanges).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Allowed_outcome_rate_matches_the_labelled_set()
    {
        var cases = Loaded(Case("a"), Case("b"), Case("c", c => c with { AllowedOutcomes = ["refused"] }));

        var values = Score(cases, 0, Result("a"), Result("b", "incomplete"), Result("c", "refused")).Values;

        Assert.Equal(2 / 3.0, values[MetricNames.AllowedOutcomeRate]!.Value, 6);
        Assert.Equal(1, values[MetricNames.ValidatedResultRate]);
    }

    [Fact]
    public void A_case_with_no_result_scores_zero()
    {
        var values = Score(Loaded(Case("a"), Case("b")), 0, Result("a")).Values;

        Assert.Equal(0.5, values[MetricNames.ValidatedResultRate]);
    }

    [Fact]
    public void Per_case_results_record_each_eligible_metric()
    {
        var result = Result("a") with { AssessmentEvidenceIds = ["incident:INC-1", "merchant:M-1", "payout:PO-1"], AssessmentSectionKeys = ["kb"] };

        var perCase = Score(Loaded(Case("a")), 0, result).PerCase["a"];

        Assert.True(perCase[MetricNames.ExpectedEvidenceCoverage]);
        Assert.False(perCase[MetricNames.ExpectedSectionRecall]);
        Assert.False(perCase.ContainsKey(MetricNames.ProposalFalsePositiveRate));
    }

    private static ToolCallRecord Call(string tool, params (string Name, string Value)[] arguments) =>
        new(tool, arguments.ToDictionary(a => a.Name, a => (string?)a.Value, StringComparer.Ordinal));

    [Fact]
    public void A_restricted_section_both_cited_and_retrieved_is_one_leak()
    {
        var result = Result("a") with { AssessmentSectionKeys = ["kr"], RetrievedSections = [new SectionHit("kr", "restricted")] };

        Assert.Single(Score(Loaded(Case("a")), 0, result).Hard.AuthorizationLeaks);
    }
}
