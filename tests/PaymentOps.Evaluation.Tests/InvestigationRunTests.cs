using System.Text.Json;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Running;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

public sealed class InvestigationRunTests
{
    private static readonly RunOutcome Valid = new([new CaseResult("c1", "assessed")], 0);
    private static readonly RunOutcome Invalid = new([new CaseResult("c1", "not-assessed:model-unavailable") { InfrastructureFailure = "model-unavailable" }], 0);

    [Fact]
    public async Task A_case_records_calls_citations_retrieved_sections_and_outcome()
    {
        var run = await AgentRunner(() => Model(Trajectory(Submission()))).RunAsync([Case()], CancellationToken.None);

        var result = Assert.Single(run.Results);
        Assert.Equal("assessed", result.Outcome);
        Assert.Equal(
            [ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.ProcessorStatus, ToolNames.KnowledgeSearch, ToolNames.SubmitAssessment],
            result.Calls.Select(c => c.Tool));
        Assert.Equal("2026-11-09", result.Calls[1].Arguments["start_date"]);
        Assert.Equal([MissingFile, "merchant:M-1042", Incident], result.AssessmentEvidenceIds);
        Assert.Equal([GeneralSection.Key], result.AssessmentSectionKeys);
        Assert.Equal([new SectionHit(GeneralSection.Key, "general")], result.RetrievedSections);
        Assert.Null(result.InfrastructureFailure);
        Assert.Equal(5, result.ModelRequests);
        Assert.NotNull(result.TtfpMs);
    }

    [Fact]
    public async Task The_judge_context_holds_the_cited_facts_and_sections_but_is_never_serialized()
    {
        var result = Assert.Single((await AgentRunner(() => Model(Trajectory(Submission()))).RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Contains("fabrikam-pay", result.CitedEvidenceFacts[MissingFile], StringComparison.Ordinal);
        Assert.Contains("\"evidence_id\":\"merchant:M-1042\"", result.CitedEvidenceFacts["merchant:M-1042"], StringComparison.Ordinal);
        Assert.Equal(GeneralSection.Content, result.CitedSectionText[GeneralSection.Key]);
        Assert.Contains("never arrived", result.AssessmentText, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("never arrived", json, StringComparison.Ordinal);
        Assert.DoesNotContain(GeneralSection.Content[..40], json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_engineer_proposal_is_approved_and_counted()
    {
        var run = await AgentRunner(() => Model(Trajectory(Submission(Ticket())))).RunAsync([Case(role: "engineer")], CancellationToken.None);

        var result = Assert.Single(run.Results);
        Assert.Equal(ProposalStates.Approved, result.ProposalState);
        Assert.Equal(1, run.TicketsInStore);
        Assert.Equal([MissingFile, Incident], result.ProposalEvidenceIds);
        Assert.Contains("Fabrikam Pay settlement file missing", result.UserVisibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("Fabrikam Pay settlement file missing", result.AssessmentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_readers_proposal_stays_a_draft_and_creates_no_ticket()
    {
        var run = await AgentRunner(() => Model(Trajectory(Submission(Ticket())))).RunAsync([Case()], CancellationToken.None);

        Assert.Equal(ProposalStates.Draft, Assert.Single(run.Results).ProposalState);
        Assert.Equal(0, run.TicketsInStore);
    }

    [Fact]
    public async Task A_search_failure_the_app_absorbed_still_invalidates_the_run()
    {
        var knowledge = new FixedKnowledge { Failure = new KnowledgeUnavailableException() };
        var submission = Submission(
            summary: Statement("M-1042 settles through Fabrikam Pay.", ["merchant:M-1042"], []),
            claims: [Statement("The merchant profile was read.", ["merchant:M-1042"], [])],
            rootCause: Statement(string.Empty, [], []),
            confidence: "medium");
        var model = () => Model(
            ScriptedChatClient.Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
            ScriptedChatClient.Call(ToolNames.KnowledgeSearch, new { query = "missing settlement file" }),
            ScriptedChatClient.Call(ToolNames.SubmitAssessment, submission));

        var result = Assert.Single((await AgentRunner(model, knowledge).RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Equal("assessed", result.Outcome);
        Assert.Equal(InfrastructureFailures.SearchUnavailable, result.InfrastructureFailure);
        Assert.NotNull(RunPlan.Invalid(new RunOutcome([result], 0)));
    }

    [Fact]
    public async Task A_model_outage_is_infrastructure_and_ends_the_run_early()
    {
        var down = () => new ScriptedChatClient(_ => throw new HttpRequestException("503"));

        var run = await AgentRunner(down).RunAsync([Case("a"), Case("b")], CancellationToken.None);

        var result = Assert.Single(run.Results);
        Assert.Equal("not-assessed:model-unavailable", result.Outcome);
        Assert.Equal(InfrastructureFailures.ModelUnavailable, result.InfrastructureFailure);
    }

    [Fact]
    public async Task Candidate_behaviour_is_never_infrastructure()
    {
        var incomplete = await AgentRunner(() => Model(Trajectory(Submission(outcome: "incomplete")))).RunAsync([Case()], CancellationToken.None);
        var unverified = await AgentRunner(() => Model(
            ScriptedChatClient.Call(ToolNames.SubmitAssessment, Submission()),
            ScriptedChatClient.Call(ToolNames.SubmitAssessment, Submission()))).RunAsync([Case()], CancellationToken.None);

        Assert.Equal("incomplete", Assert.Single(incomplete.Results).Outcome);
        Assert.Equal("not-assessed:assessment-unverified", Assert.Single(unverified.Results).Outcome);
        Assert.All(incomplete.Results.Concat(unverified.Results), r => Assert.Null(r.InfrastructureFailure));
    }

    [Fact]
    public async Task A_run_invalid_once_is_repeated()
    {
        var runner = new ScriptedRunner(Invalid, Valid);

        var runs = await RunPlan.RunAsync(runner, [Case()], 1, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(Valid, Assert.Single(runs));
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    public async Task A_run_invalid_twice_could_not_evaluate()
    {
        var runner = new ScriptedRunner(Valid, Invalid, Invalid);

        var ex = await Assert.ThrowsAsync<CouldNotEvaluateException>(() => RunPlan.RunAsync(runner, [Case()], 3, TimeSpan.Zero, CancellationToken.None));

        Assert.Contains("model-unavailable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Three_runs_produce_three_outcomes()
    {
        var runs = await RunPlan.RunAsync(new ScriptedRunner(Valid, Valid, Valid), [Case()], 3, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(3, runs.Count);
    }

    [Fact]
    public void Time_to_first_progress_starts_at_the_first_agent_driven_event()
    {
        var clock = new Queue<long>([100, 900, 1500]);
        var sink = new FirstProgress(() => clock.Dequeue());

        sink.Report(new InvestigationProgress.LimitReached());
        sink.Report(new InvestigationProgress.ToolStarted(ToolNames.MerchantProfile, "Merchant profile"));
        sink.Report(new InvestigationProgress.Submitting());

        Assert.Equal(900, sink.FirstMs);
    }

    [Fact]
    public void A_direct_submission_counts_as_the_first_progress()
    {
        var sink = new FirstProgress(() => 300);

        sink.Report(new InvestigationProgress.Submitting());

        Assert.Equal(300, sink.FirstMs);
    }

    // A bug in the lookup is the system's failure, scored as such, not the environment's.
    [Fact]
    public async Task Only_a_real_search_failure_is_infrastructure()
    {
        var knowledge = new FixedKnowledge { Failure = new InvalidOperationException("bug") };

        var result = Assert.Single((await AgentRunner(() => Model(Trajectory(Submission())), knowledge).RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Null(result.InfrastructureFailure);
    }
}
