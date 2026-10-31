using Microsoft.Extensions.AI;

using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Challenger;
using PaymentOps.Evaluation.Running;
using PaymentOps.Tools;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

public sealed class FixedWorkflowTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static ChatResponse Scope(string merchants, string? start = "2026-11-08", string? end = "2026-11-10") => ScriptedChatClient.Text(
        $$"""{"merchant_ids":[{{merchants}}],"processor_ids":[],"start_date":{{(start is null ? "null" : $"\"{start}\"")}},"end_date":{{(end is null ? "null" : $"\"{end}\"")}}}""");

    private static ChatResponse Submit(object submission) => ScriptedChatClient.Call(ToolNames.SubmitAssessment, submission);

    // The challenger is given no screen, so it withholds every knowledge section and can't cite one; this cites data only.
    private static object DataOnly() => Submission(
        summary: Statement("The 9 November Fabrikam file never arrived.", [MissingFile], []),
        rootCause: Statement("A Fabrikam settlement outage stopped the file.", [MissingFile, Incident], []));

    private static FixedWorkflowCaseRunner Challenger(Func<ScriptedChatClient> model) =>
        new(Release, Endpoints, new UnusedCredential(), _ => model(), _ => new FixedKnowledge(GeneralSection));

    private static async Task<CaseResult> RunAsync(Func<ScriptedChatClient> model, string role = "reader") =>
        Assert.Single((await Challenger(model).RunAsync([Case(role: role)], CancellationToken.None)).Results);

    [Fact]
    public async Task The_fan_out_calls_profile_then_batches_status_errors_and_knowledge()
    {
        var result = await RunAsync(() => Model(Scope("\"M-1042\""), Submit(DataOnly())));

        var reads = result.Calls.Where(c => c.Tool != ToolNames.SubmitAssessment).ToList();
        Assert.Equal(ToolNames.MerchantProfile, reads[0].Tool);
        Assert.Equal("M-1042", reads[0].Arguments["merchant_id"]);
        Assert.Equal(ToolNames.SettlementBatches, reads[1].Tool);
        Assert.Equal(("2026-11-08", "2026-11-10"), (reads[1].Arguments["start_date"], reads[1].Arguments["end_date"]));
        Assert.Contains(reads, c => c.Tool == ToolNames.ProcessorStatus && c.Arguments["processor_id"] == Fabrikam);
        Assert.Contains(reads, c => c.Tool == ToolNames.ErrorEvents && c.Arguments["processor_id"] == Fabrikam);
        Assert.Equal(ToolNames.KnowledgeSearch, reads[^1].Tool);
        Assert.Equal("assessed", result.Outcome);
        Assert.Equal(2, result.ModelRequests);
    }

    [Fact]
    public async Task Without_a_merchant_only_knowledge_is_searched()
    {
        var result = await RunAsync(() => Model(Scope(string.Empty, null, null), Submit(Refusal())));

        Assert.Equal([ToolNames.KnowledgeSearch, ToolNames.SubmitAssessment], result.Calls.Select(c => c.Tool));
        Assert.Equal("refused", result.Outcome);
    }

    [Fact]
    public void The_window_is_clamped_to_31_days_and_today() =>
        Assert.Equal((new DateOnly(2026, 10, 12), Today), ScopeExtraction.Window(new ExtractedScope([], [], "2026-09-01", "2026-12-01"), Today));

    [Fact]
    public void No_window_means_the_last_7_days()
    {
        Assert.Equal((new DateOnly(2026, 11, 5), Today), ScopeExtraction.Window(new ExtractedScope([], [], null, null), Today));
        Assert.Equal((new DateOnly(2026, 11, 5), Today), ScopeExtraction.Window(new ExtractedScope([], [], "last week", null), Today));
    }

    [Fact]
    public async Task At_most_three_merchants_are_investigated()
    {
        var result = await RunAsync(() => Model(Scope("\"M-1042\",\"M-6230\",\"M-2087\",\"M-3310\""), Submit(Submission())));

        Assert.Equal(["M-1042", "M-6230", "M-2087"], result.Calls.Where(c => c.Tool == ToolNames.MerchantProfile).Select(c => c.Arguments["merchant_id"]));
        Assert.True(result.Calls.Count(c => c.Tool != ToolNames.SubmitAssessment) <= 10);
        Assert.Equal(ToolNames.KnowledgeSearch, result.Calls.Last(c => c.Tool != ToolNames.SubmitAssessment).Tool);
    }

    [Fact]
    public async Task The_submission_goes_through_the_real_validator_with_one_correction()
    {
        var bad = Submission(summary: Statement("The file never arrived.", ["file:NO-SUCH-FILE"], []), confidence: "low");

        var corrected = await RunAsync(() => Model(Scope("\"M-1042\""), Submit(bad), Submit(DataOnly())));
        var twice = await RunAsync(() => Model(Scope("\"M-1042\""), Submit(bad), Submit(bad)));

        Assert.Equal("assessed", corrected.Outcome);
        Assert.Equal("not-assessed:assessment-unverified", twice.Outcome);
    }

    [Fact]
    public async Task The_extraction_never_sees_labels()
    {
        ScriptedChatClient? model = null;
        await RunAsync(() => model = Model(Scope("\"M-1042\""), Submit(Submission())));

        var extraction = model!.Requests[0];
        var text = string.Join('\n', extraction.Messages.Select(m => m.Text));
        Assert.Contains("Why were M-1042's payouts delayed yesterday?", text, StringComparison.Ordinal);
        Assert.Contains("2026-11-11", text, StringComparison.Ordinal);
        Assert.Contains("fabrikam-pay (Fabrikam Pay)", text, StringComparison.Ordinal);
        Assert.Empty(extraction.Tools);
        Assert.DoesNotContain("allowed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expected", text, StringComparison.OrdinalIgnoreCase);
    }

    // A ticket needs a knowledge citation, and without a screen the challenger has none it may cite: it fails closed.
    [Fact]
    public async Task Without_a_screen_the_challenger_cannot_ground_a_proposal()
    {
        var engineer = await Challenger(() => Model(Scope("\"M-1042\""), Submit(Submission(Ticket())), Submit(Submission(Ticket())))).RunAsync([Case(role: "engineer")], CancellationToken.None);

        var result = Assert.Single(engineer.Results);
        Assert.Equal("not-assessed:assessment-unverified", result.Outcome);
        Assert.Equal(ProposalStates.None, result.ProposalState);
        Assert.Equal(0, engineer.TicketsInStore);
    }

    [Fact]
    public void The_challenger_prompts_are_versioned()
    {
        Assert.Equal(("fixed-workflow", "1.0.0"), (FixedWorkflow.Prompt.Id, FixedWorkflow.Prompt.Version));
        Assert.Equal(("scope-extraction", "1.0.0"), (ScopeExtraction.Prompt.Id, ScopeExtraction.Prompt.Version));
        Assert.DoesNotContain("Choose tools", FixedWorkflow.Prompt.Body, StringComparison.Ordinal);
        Assert.Contains("Finish by calling submit_assessment exactly once.", FixedWorkflow.Prompt.Body, StringComparison.Ordinal);
    }
}
