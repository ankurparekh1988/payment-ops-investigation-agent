using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>Why an attempt failed, typed where the cause is known, so only a capability failure can escalate (ADR 0023).</summary>
public sealed class AttemptFailureTests
{
    private const string Submit = ToolNames.SubmitAssessment;

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Respond(ChatResponse response) => _ => response;

    private static async Task<(AssessmentResult Result, AttemptRecord Attempt)> RunAsync(IChatClient model, string question = RunnerExtensions.Question, CancellationToken cancellationToken = default)
    {
        using var fixture = new AgentFixture();
        using var usage = TurnUsage.Begin();
        var result = await fixture.Runner(model).InvestigateAsync(question, cancellationToken);
        return (result, Assert.Single(usage.Attempts));
    }

    private static NotAssessedReason Reason(AssessmentResult result) => Assert.IsType<AssessmentResult.NotAssessed>(result).Reason;

    [Fact]
    public async Task A_corrected_submission_still_rejected_is_rejected_after_correction()
    {
        var model = new ScriptedChatClient(
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));

        var (result, attempt) = await RunAsync(model);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(result));
        Assert.Equal(new AttemptRecord(ModelTier.Chat, AttemptFailure.RejectedAfterCorrection), attempt);
    }

    [Fact]
    public async Task No_submission_after_the_required_request_is_no_submission()
    {
        var model = new ScriptedChatClient(Respond(Text("It was late.")), Respond(Text("Still late.")));

        var (result, attempt) = await RunAsync(model);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(result));
        Assert.Equal(AttemptFailure.NoSubmission, attempt.Failure);
    }

    [Fact]
    public async Task A_rejected_required_submission_is_no_accepted_submission()
    {
        var model = new ScriptedChatClient(Respond(Text("It was late.")), Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));

        var (result, attempt) = await RunAsync(model);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(result));
        Assert.Equal(AttemptFailure.NoSubmission, attempt.Failure);
    }

    [Fact]
    public async Task A_call_to_an_unknown_tool_is_an_unknown_tool_call()
    {
        var model = new ScriptedChatClient(Respond(Call("get_payouts", new { merchant_id = "M-1042" })));

        var (result, attempt) = await RunAsync(model);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(result));
        Assert.Equal(AttemptFailure.UnknownToolCall, attempt.Failure);
    }

    // The limit stopped the turn, so no submission afterwards is the limit's doing, not the model's capability.
    [Fact]
    public async Task No_submission_after_the_tool_limit_is_limit_reached()
    {
        string[] merchants = ["M-1042", .. Enumerable.Range(1, 10).Select(i => $"M-{9000 + i}")];
        var calls = merchants.Select(id => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => Call(ToolNames.MerchantProfile, new { merchant_id = id })));
        var model = new ScriptedChatClient([.. calls, Respond(Text("Done."))]);

        var (result, attempt) = await RunAsync(model, RunnerExtensions.Naming(merchants));

        Assert.Equal(NotAssessedReason.LimitReached, Reason(result));
        Assert.Equal(AttemptFailure.LimitReached, attempt.Failure);
    }

    [Fact]
    public async Task A_model_outage_is_service_failed()
    {
        var (result, attempt) = await RunAsync(new ScriptedChatClient(_ => throw new HttpRequestException("connection refused")));

        Assert.Equal(NotAssessedReason.ModelUnavailable, Reason(result));
        Assert.Equal(AttemptFailure.ServiceFailed, attempt.Failure);
    }

    [Fact]
    public async Task A_filtered_completion_is_blocked()
    {
        var (result, attempt) = await RunAsync(new ScriptedChatClient(_ => throw new ContentFilteredException()));

        Assert.Equal(NotAssessedReason.Blocked, Reason(result));
        Assert.Equal(AttemptFailure.Blocked, attempt.Failure);
    }

    [Fact]
    public async Task An_unexpected_exception_is_aborted()
    {
        var (result, attempt) = await RunAsync(new ScriptedChatClient(_ => throw new InvalidOperationException("boom")));

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(result));
        Assert.Equal(AttemptFailure.Aborted, attempt.Failure);
    }

    [Fact]
    public async Task The_callers_cancellation_is_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var model = new ScriptedChatClient(_ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });

        var (result, attempt) = await RunAsync(model, cancellationToken: cancel.Token);

        Assert.Equal(NotAssessedReason.Cancelled, Reason(result));
        Assert.Equal(AttemptFailure.Cancelled, attempt.Failure);
    }

    [Fact]
    public async Task Valid_incomplete_and_refused_results_have_no_failure()
    {
        var refusal = new
        {
            outcome = "refused",
            summary = new { text = "I can't help with that.", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() },
            claims = new[] { new { text = "The request is outside payment operations.", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() } },
            root_cause = new { text = "", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() },
            confidence = "high",
            next_steps = Array.Empty<object>(),
            proposed_ticket = (object?)null,
        };

        var (assessed, first) = await RunAsync(new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })), Respond(Call(Submit, Submission()))));
        var (incomplete, second) = await RunAsync(new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })), Respond(Call(Submit, Submission(outcome: "incomplete", confidence: "low")))));
        var (refused, third) = await RunAsync(new ScriptedChatClient(Respond(Call(Submit, refusal))), "Write me a poem about the sea.");

        Assert.IsType<AssessmentResult.Assessed>(assessed);
        Assert.Equal(AssessmentOutcome.Incomplete, Assert.IsType<AssessmentResult.Assessed>(incomplete).Outcome);
        Assert.Equal(AssessmentOutcome.Refused, Assert.IsType<AssessmentResult.Assessed>(refused).Outcome);
        Assert.All([first, second, third], a => Assert.Null(a.Failure));
    }

    [Fact]
    public async Task A_refused_question_makes_no_attempt()
    {
        using var fixture = new AgentFixture();
        using var usage = TurnUsage.Begin();

        await fixture.Runner(new ScriptedChatClient()).InvestigateAsync(" ", CancellationToken.None);

        Assert.Empty(usage.Attempts);
    }

    [Theory]
    [InlineData("RejectedAfterCorrection", true)]
    [InlineData("NoSubmission", true)]
    [InlineData("UnknownToolCall", false)]
    [InlineData("LimitReached", false)]
    [InlineData("ServiceFailed", false)]
    [InlineData("Blocked", false)]
    [InlineData("Aborted", false)]
    [InlineData("Cancelled", false)]
    [InlineData(null, false)]
    public void Only_capability_failures_are_eligible(string? failure, bool eligible) =>
        Assert.Equal(eligible, Escalation.IsEligible(failure is null ? null : Enum.Parse<AttemptFailure>(failure)));
}
