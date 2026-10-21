using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

public sealed class InvestigationAgentRunnerTests
{
    private const string Submit = ToolNames.SubmitAssessment;

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Respond(ChatResponse response) => _ => response;

    [Fact]
    public async Task A_valid_submission_ends_the_turn_with_no_further_model_request()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Call(Submit, Submission())));

        var result = await fixture.Runner(model).InvestigateAsync("Why was M-1042's payout late yesterday?", CancellationToken.None);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal("merchant:M-1042", Assert.Single(assessed.Summary.Evidence).Id);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task The_agent_is_told_the_investigation_date_and_reasons_with_no_effort()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })), Respond(Call(Submit, Submission())));

        await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        var options = model.Requests[0].Options!;
        Assert.Contains("Today is 2026-11-11.", options.Instructions, StringComparison.Ordinal);
        Assert.Equal(ReasoningEffort.None, options.Reasoning!.Effort);
        Assert.Equal([ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.ProcessorStatus, ToolNames.ErrorEvents, ToolNames.KnowledgeSearch, Submit], model.Requests[0].Tools);
    }

    // After an invalid submission the model may only correct it: the read tools aren't offered again.
    [Fact]
    public async Task An_invalid_submission_gets_one_correction_with_only_submit_assessment_offered()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
            Respond(Call(Submit, Submission())));

        var result = await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal([Submit], model.Requests[2].Tools);
        Assert.Equal(ChatToolMode.RequireSpecific(Submit), model.Requests[2].Mode);
        Assert.Contains(ToolResults(model.Requests[2].Messages), r => r.Contains("'payout:PO-404' wasn't returned by a tool", StringComparison.Ordinal));
        Assert.Equal(3, model.Requests.Count);
    }

    [Fact]
    public async Task A_second_invalid_submission_leaves_the_assessment_unverified()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));

        var result = await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    [Fact]
    public async Task An_answer_in_plain_text_gets_one_request_that_allows_only_submit_assessment()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Text("It was an outage.")),
            Respond(Call(Submit, Submission())));

        var result = await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal([Submit], model.Requests[2].Tools);
    }

    [Fact]
    public async Task Nothing_runs_after_a_valid_submission_in_the_same_response()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Calls((Submit, Submission(), null), (Submit, Submission(claimEvidence: ["payout:PO-404"]), null), (ToolNames.MerchantProfile, new { merchant_id = "M-2087" }, null))));

        var result = await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal("merchant:M-1042", Assert.Single(assessed.Claims[0].Evidence).Id);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task An_unauthorized_user_never_reaches_the_model()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient();

        var result = await fixture.Runner(model, TestUser.General).InvestigateAsync("q", CancellationToken.None);

        Assert.Equal(NotAssessedReason.NotAuthorized, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Empty(model.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_question_is_invalid(string question)
    {
        using var fixture = new AgentFixture();

        var result = await fixture.Runner(new ScriptedChatClient()).InvestigateAsync(question, CancellationToken.None);

        Assert.Equal(NotAssessedReason.InvalidQuestion, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    [Fact]
    public async Task A_model_service_failure_is_reported_as_unavailable()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(_ => throw new HttpRequestException("connection refused"));

        var result = await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        Assert.Equal(NotAssessedReason.ModelUnavailable, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }
}
