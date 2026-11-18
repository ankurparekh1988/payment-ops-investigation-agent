using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>The cascade: one attempt on the small model, and one on chat only after an eligible failure, within one budget.</summary>
public sealed class RoutingTests
{
    private const string Submit = ToolNames.SubmitAssessment;

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Respond(ChatResponse response) => _ => response;

    // Fresh responses for each test: the tool loop adds to the messages it's given.
    private static Func<IReadOnlyList<ChatMessage>, ChatResponse>[] Valid =>
    [
        Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
        Respond(Call(Submit, Submission())),
    ];

    // Rejected, then rejected again after its correction: the eligible failure most scripts use.
    private static Func<IReadOnlyList<ChatMessage>, ChatResponse>[] Rejected =>
    [
        Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
        Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
    ];

    private sealed record Run(AssessmentResult Result, IReadOnlyList<AttemptRecord> Attempts, string? Escalation);

    private static async Task<Run> InvestigateAsync(
        ScriptedChatClient small, IChatClient chat, RoutingMode mode = RoutingMode.Cascade, InvestigationLimits? limits = null, TimeProvider? time = null, string question = RunnerExtensions.Question, CancellationToken cancellationToken = default)
    {
        using var fixture = new AgentFixture();
        using var usage = TurnUsage.Begin();
        var result = await fixture.Runner(new ModelRouting(mode, chat, small), time: time, limits: limits).InvestigateAsync(question, cancellationToken);
        return new Run(result, usage.Attempts, usage.Escalation);
    }

    private static NotAssessedReason Reason(AssessmentResult result) => Assert.IsType<AssessmentResult.NotAssessed>(result).Reason;

    [Fact]
    public async Task Off_mode_runs_only_the_chat_model()
    {
        var small = new ScriptedChatClient();
        var chat = new ScriptedChatClient(Valid);

        var run = await InvestigateAsync(small, chat, RoutingMode.Off);

        Assert.IsType<AssessmentResult.Assessed>(run.Result);
        Assert.Empty(small.Requests);
        Assert.Equal(2, chat.Requests.Count);
        Assert.Equal([new AttemptRecord(ModelTier.Chat, null)], run.Attempts);
        Assert.Equal("none", run.Escalation);
    }

    [Fact]
    public async Task A_valid_small_result_is_the_answer()
    {
        var chat = new ScriptedChatClient();

        var run = await InvestigateAsync(new ScriptedChatClient(Valid), chat);

        Assert.IsType<AssessmentResult.Assessed>(run.Result);
        Assert.Empty(chat.Requests);
        Assert.Equal([new AttemptRecord(ModelTier.Small, null)], run.Attempts);
    }

    [Fact]
    public async Task Eligible_failure_escalates_once_to_chat()
    {
        var chat = new ScriptedChatClient(Valid);

        var run = await InvestigateAsync(new ScriptedChatClient(Rejected), chat);

        Assert.IsType<AssessmentResult.Assessed>(run.Result);
        Assert.Equal([new AttemptRecord(ModelTier.Small, AttemptFailure.RejectedAfterCorrection), new AttemptRecord(ModelTier.Chat, null)], run.Attempts);
        Assert.Equal("escalated", run.Escalation);
    }

    [Fact]
    public async Task A_second_eligible_failure_does_not_escalate_again()
    {
        var run = await InvestigateAsync(new ScriptedChatClient(Rejected), new ScriptedChatClient(Rejected));

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(run.Result));
        Assert.Equal(2, run.Attempts.Count);
    }

    // The small model's submission, tool calls and evidence ids are never shown to chat.
    [Fact]
    public async Task The_escalated_attempt_starts_from_the_question_alone()
    {
        var small = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));
        var chat = new ScriptedChatClient(Valid);

        await InvestigateAsync(small, chat);

        var first = chat.Requests[0].Messages;
        Assert.Equal(RunnerExtensions.Question, Assert.Single(first).Text);
    }

    // An evidence id the small model was given is not this attempt's: it has to be returned to chat again.
    [Fact]
    public async Task Chat_cannot_cite_evidence_only_the_small_model_was_given()
    {
        var small = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Text("Late.")),
            Respond(Text("Still late.")));
        var chat = new ScriptedChatClient(Respond(Call(Submit, Submission())), Respond(Call(Submit, Submission())));

        var run = await InvestigateAsync(small, chat);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(run.Result));
        Assert.Contains(ToolResults(chat.Requests[1].Messages), r => r.Contains("'merchant:M-1042' wasn't returned by a tool", StringComparison.Ordinal));
    }

    public static TheoryData<string> IneligibleScenarios() =>
        ["unknown_tool", "service_failed", "blocked", "aborted", "limit_reached", "incomplete", "refused"];

    private static ScriptedChatClient Scenario(string name)
    {
        string[] merchants = ["M-1042", .. Enumerable.Range(1, 10).Select(i => $"M-{9000 + i}")];
        return name switch
        {
            "unknown_tool" => new ScriptedChatClient(Respond(Call("get_payouts", new { merchant_id = "M-1042" }))),
            "service_failed" => new ScriptedChatClient(_ => throw new HttpRequestException("connection refused")),
            "blocked" => new ScriptedChatClient(_ => throw new ContentFilteredException()),
            "aborted" => new ScriptedChatClient(_ => throw new InvalidOperationException("boom")),
            "limit_reached" => new ScriptedChatClient(
                [.. merchants.Select(id => Respond(Call(ToolNames.MerchantProfile, new { merchant_id = id }))), Respond(Text("Done."))]),
            "incomplete" => new ScriptedChatClient(
                Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })), Respond(Call(Submit, Submission(outcome: "incomplete", confidence: "low")))),
            "refused" => new ScriptedChatClient(Respond(Call(Submit, new
            {
                outcome = "refused",
                summary = new { text = "I can't help with that.", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() },
                claims = new[] { new { text = "The request is outside payment operations.", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() } },
                root_cause = new { text = "", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() },
                confidence = "high",
                next_steps = Array.Empty<object>(),
                proposed_ticket = (object?)null,
            }))),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(nameof(IneligibleScenarios))]
    public async Task Ineligible_failures_and_validated_results_do_not_escalate(string scenario)
    {
        string[] merchants = ["M-1042", .. Enumerable.Range(1, 10).Select(i => $"M-{9000 + i}")];
        var chat = new ScriptedChatClient();

        var run = await InvestigateAsync(Scenario(scenario), chat, question: RunnerExtensions.Naming(merchants));

        Assert.Empty(chat.Requests);
        Assert.Single(run.Attempts);
        Assert.Equal("none", run.Escalation);
    }

    [Fact]
    public async Task Cancellation_does_not_escalate()
    {
        using var cancel = new CancellationTokenSource();
        var small = new ScriptedChatClient(_ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });
        var chat = new ScriptedChatClient();

        var run = await InvestigateAsync(small, chat, cancellationToken: cancel.Token);

        Assert.Equal(NotAssessedReason.Cancelled, Reason(run.Result));
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task Refused_escalation_returns_the_first_result()
    {
        var time = new FakeTimeProvider();
        var small = new ScriptedChatClient(
            _ =>
            {
                time.Advance(TimeSpan.FromSeconds(41));
                return Call(Submit, Submission(claimEvidence: ["payout:PO-404"]));
            },
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));
        var chat = new ScriptedChatClient();

        var run = await InvestigateAsync(small, chat, time: time);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(run.Result));
        Assert.Empty(chat.Requests);
        Assert.Equal("refused:time", run.Escalation);
        Assert.Equal([new AttemptRecord(ModelTier.Small, AttemptFailure.RejectedAfterCorrection)], run.Attempts);
    }

    [Fact]
    public async Task Escalation_is_refused_when_attempt_twos_first_call_cannot_fit()
    {
        var limits = new InvestigationLimits { MaxInvestigationTokens = 70_000, MinEscalationTokens = 10_000 };
        static ChatResponse Heavy(IReadOnlyList<ChatMessage> _)
        {
            var response = Call(Submit, Submission(claimEvidence: ["payout:PO-404"]));
            response.Usage = new UsageDetails { InputTokenCount = 30_000, OutputTokenCount = 0 };
            return response;
        }

        var chat = new ScriptedChatClient();

        var run = await InvestigateAsync(new ScriptedChatClient(Heavy, Heavy), chat, limits: limits);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Reason(run.Result));
        Assert.Equal("refused:first_call", run.Escalation);
        Assert.Empty(chat.Requests);
    }

    // Three tool calls on the small model leave seven for chat: its eighth is refused as the step limit.
    [Fact]
    public async Task Attempt_two_never_gets_a_fresh_budget()
    {
        string[] merchants = ["M-1042", .. Enumerable.Range(1, 10).Select(i => $"M-{9000 + i}")];
        var small = new ScriptedChatClient(
            [.. merchants.Take(3).Select(id => Respond(Call(ToolNames.MerchantProfile, new { merchant_id = id }))), Respond(Text("Late.")), Respond(Text("Still late."))]);
        var chat = new ScriptedChatClient(
            [.. merchants.Take(8).Select(id => Respond(Call(ToolNames.MerchantProfile, new { merchant_id = id }))), Respond(Call(Submit, Submission()))]);

        var run = await InvestigateAsync(small, chat, question: RunnerExtensions.Naming(merchants));

        Assert.Equal(AssessmentOutcome.Incomplete, Assert.IsType<AssessmentResult.Assessed>(run.Result).Outcome);
        Assert.Contains("step limit", ToolResults(chat.Requests[8].Messages)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_attempt_shares_the_deadline()
    {
        var time = new FakeTimeProvider();
        var small = new ScriptedChatClient(
            _ =>
            {
                time.Advance(TimeSpan.FromSeconds(30));
                return Call(Submit, Submission(claimEvidence: ["payout:PO-404"]));
            },
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));
        using var chat = new ProgressTests.WaitingChatClient();

        using var fixture = new AgentFixture();
        var running = fixture.Runner(new ModelRouting(RoutingMode.Cascade, chat, small), time: time).InvestigateAsync(RunnerExtensions.Question, CancellationToken.None);
        await chat.Started.Task;
        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(NotAssessedReason.LimitReached, Reason(await running));
    }

    [Fact]
    public async Task Cancel_during_the_second_attempt_is_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        using var chat = new ProgressTests.WaitingChatClient();

        using var fixture = new AgentFixture();
        var running = fixture.Runner(new ModelRouting(RoutingMode.Cascade, chat, new ScriptedChatClient(Rejected))).InvestigateAsync(RunnerExtensions.Question, cancel.Token);
        await chat.Started.Task;
        await cancel.CancelAsync();

        Assert.Equal(NotAssessedReason.Cancelled, Reason(await running));
    }
}
