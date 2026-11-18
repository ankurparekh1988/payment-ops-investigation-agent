using System.ClientModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests.Telemetry;

[CollectionDefinition(nameof(ProcessWideMetrics), DisableParallelization = true)]
public sealed class ProcessWideMetrics;

/// <summary>
/// Every investigation that runs is one trace, one count by outcome and one decision record, whatever its outcome;
/// none of them carries the question, the answer, tool output or who asked. Runs alone: the metrics are process-wide.
/// </summary>
[Collection(nameof(ProcessWideMetrics))]
public sealed class InvestigationTraceTests : IDisposable
{
    private const string QuestionSentinel = "zzq-question";
    private const string AnswerSentinel = "zzq-answer";
    private const string ToolSentinel = "zzq-tool";
    private const string UserSentinel = "zzq-user";

    private static readonly ActivitySource TestSource = new("PaymentOps.Agent.Tests.Trace");

    private readonly List<Activity> _activities = [];
    private readonly List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public InvestigationTraceTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is AgentTelemetry.Name || source == TestSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_activities)
                {
                    _activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AgentTelemetry.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (_measurements)
            {
                _measurements.Add((instrument.Name, value, tags.ToArray()));
            }
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    private sealed record Turn(
        string Outcome,
        Func<ScriptedChatClient>? Model = null,
        IChatClient? FilteredModel = null,
        IQuestionScreen? Questions = null,
        bool Cancelled = false,
        int MaxQuestions = 10,
        Func<ModelRouting>? Routing = null);

    private static readonly RetrievedSection Section = new("k1", "d1", "Missing file guide", "Guide > Missing file", $"Check the processor's file feed. {ToolSentinel}", "General", null);

    public static TheoryData<string> Outcomes() => [.. Turns.Keys];

    private static readonly Dictionary<string, Turn> Turns = new(StringComparer.Ordinal)
    {
        ["assessed"] = new("assessed", () => new(
            _ => Call(ToolNames.KnowledgeSearch, new { query = "missing settlement file" }),
            _ => Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
            _ => Call(ToolNames.SubmitAssessment, Submission(summaryText: $"The batch didn't arrive. {AnswerSentinel}")))),
        ["question blocked"] = new("blocked", Questions: new ScriptedQuestionScreen(QuestionVerdict.Harmful)),
        ["self-harm"] = new("blocked_self_harm", Questions: new ScriptedQuestionScreen(QuestionVerdict.SelfHarm)),
        ["question unscreened"] = new("unscreened", Questions: new ScriptedQuestionScreen(QuestionVerdict.Unavailable)),
        ["completion filtered"] = new("blocked", FilteredModel: ContentFilter.Checked(new ScriptedChatClient(_ => ModelFailureTests.Filtered()))),
        ["model unavailable"] = new("model_unavailable", () => new(_ => throw new ClientResultException(new FakePipelineResponse(400, "{\"error\":{\"code\":\"invalid_request\"}}")))),
        ["cancelled"] = new("cancelled", Cancelled: true),
        ["conversation full"] = new("conversation_limit", MaxQuestions: 0),

        // The small model's submission is rejected twice, so chat makes the one escalated attempt.
        ["escalated"] = new("assessed", Routing: () => new ModelRouting(
            RoutingMode.Cascade,
            new ScriptedChatClient(
                _ => Call(ToolNames.KnowledgeSearch, new { query = "missing settlement file" }),
                _ => Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
                _ => Call(ToolNames.SubmitAssessment, Submission(summaryText: $"The batch didn't arrive. {AnswerSentinel}"))),
            new ScriptedChatClient(
                _ => Call(ToolNames.SubmitAssessment, Submission(claimEvidence: ["payout:PO-404"], summaryText: AnswerSentinel)),
                _ => Call(ToolNames.SubmitAssessment, Submission(claimEvidence: ["payout:PO-404"], summaryText: AnswerSentinel))))),
    };

    private static (InvestigationService Service, RecordingLogger<InvestigationService> Logger) Service(AgentFixture fixture, Turn turn)
    {
        var limits = Options.Create(new InvestigationLimits());
        var model = turn.FilteredModel ?? turn.Model?.Invoke() ?? new ScriptedChatClient(_ => Call(ToolNames.SubmitAssessment, Submission()));
        var user = TestUser.Reader with { UserId = UserSentinel };
        var logger = new RecordingLogger<InvestigationService>();
        var service = new InvestigationService(
            turn.Routing is { } routing ? fixture.Runner(routing(), user: user, questionScreen: turn.Questions) : fixture.Runner(model, user: user, questionScreen: turn.Questions),
            fixture.FixedPrompt,
            new SourceService(FakeKnowledgeLookup.Returning(), user, limits, NullLogger<SourceService>.Instance),
            new Conversation(),
            Options.Create(new ConversationOptions { MaxQuestions = turn.MaxQuestions }),
            limits,
            user,
            new ProposalStore(TimeProvider.System, Options.Create(new ApprovalOptions()), AiReleaseLoader.Load(new EmbeddedAiReleaseSource())),
            logger,
            Options.Create(new CostEstimateOptions()),
            TraceContext.From(AgentFixture.Release));
        return (service, logger);
    }

    private async Task<(Activity Root, IReadOnlyList<Activity> Trace, RecordingLogger<InvestigationService> Logger)> RunAsync(Turn turn)
    {
        using var fixture = new AgentFixture(knowledge: FakeKnowledgeLookup.Returning(Section));
        var (service, logger) = Service(fixture, turn);
        using var cancel = new CancellationTokenSource();
        if (turn.Cancelled)
        {
            await cancel.CancelAsync();
        }

        ActivityTraceId traceId;
        using (var parent = TestSource.StartActivity("test")!)
        {
            traceId = parent.TraceId;
            await service.InvestigateAsync($"{RunnerExtensions.Question} {QuestionSentinel}", null, cancel.Token);
        }

        List<Activity> trace;
        lock (_activities)
        {
            trace = [.. _activities.Where(a => a.TraceId == traceId && a.Source.Name == AgentTelemetry.Name)];
        }

        return (Assert.Single(trace, a => a.OperationName == "investigation"), trace, logger);
    }

    private IReadOnlyList<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> Measured(string name)
    {
        lock (_measurements)
        {
            return [.. _measurements.Where(m => m.Name == name)];
        }
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Every_outcome_is_traced_counted_and_recorded(string row)
    {
        var turn = Turns[row];

        var (root, _, logger) = await RunAsync(turn);

        Assert.Equal(turn.Outcome, root.GetTagItem("paymentops.outcome"));
        Assert.NotNull(root.GetTagItem("paymentops.investigation.id"));
        Assert.Contains(Measured("paymentops.investigations"), m => m.Value == 1 && m.Tags.Contains(new("outcome", turn.Outcome)));
        Assert.Single(logger.Entries, e => e.Message.Contains($"decision: {turn.Outcome}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_investigation_is_one_trace_with_its_screens_searches_and_tools()
    {
        var (root, trace, _) = await RunAsync(Turns["assessed"]);

        Assert.Contains(trace, a => a.OperationName == "screen.question");
        Assert.Contains(trace, a => a.OperationName == "knowledge.search");
        Assert.Contains(trace, a => a.OperationName == $"tool.{ToolNames.KnowledgeSearch}" && Equals(a.GetTagItem("paymentops.tool.outcome"), "ok"));
        Assert.All(trace.Where(a => a != root), a => Assert.Equal(root.TraceId, a.TraceId));
    }

    [Fact]
    public async Task Decision_record_names_routing_attempts_escalation_and_reused_reads()
    {
        var (root, _, logger) = await RunAsync(Turns["escalated"]);

        var decision = Assert.Single(logger.Entries, e => e.Message.Contains("decision:", StringComparison.Ordinal)).Message;
        Assert.Contains("routing cascade, attempts small:rejected_after_correction>chat:assessed, escalation escalated, reused reads 0", decision, StringComparison.Ordinal);
        Assert.Equal("small:rejected_after_correction>chat:assessed", root.GetTagItem("paymentops.attempts"));
        Assert.Contains(Measured("paymentops.investigation.escalations"), m => m.Tags.Contains(new("decision", "escalated")));
    }

    [Fact]
    public async Task Decision_record_without_routing_names_one_chat_attempt()
    {
        var (_, _, logger) = await RunAsync(Turns["assessed"]);

        var decision = Assert.Single(logger.Entries, e => e.Message.Contains("decision:", StringComparison.Ordinal)).Message;
        Assert.Contains("routing off, attempts chat:assessed, escalation none", decision, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_escalated_investigation_has_one_id_and_one_trace_with_a_span_per_attempt()
    {
        var (root, trace, logger) = await RunAsync(Turns["escalated"]);

        var attempts = trace.Where(a => a.OperationName == "attempt").OrderBy(a => a.StartTimeUtc).ToList();
        Assert.Equal(["small", "chat"], attempts.Select(a => a.GetTagItem("paymentops.attempt.tier")));
        Assert.Equal(["rejected_after_correction", "none"], attempts.Select(a => a.GetTagItem("paymentops.attempt.failure")));
        Assert.All(attempts, a => Assert.Equal(root.SpanId, a.ParentSpanId));
        Assert.Single(trace, a => a.OperationName == "screen.question");
        Assert.Single(logger.Entries, e => e.Message.Contains("decision:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_trace_names_the_prompt_deployment_and_index_it_used()
    {
        var release = AgentFixture.Release;

        var (root, trace, _) = await RunAsync(Turns["assessed"]);

        Assert.Equal(InvestigationAgentRunner.PromptId, root.GetTagItem("paymentops.prompt.id"));
        Assert.Equal(release.Prompts[InvestigationAgentRunner.PromptId].Version, root.GetTagItem("paymentops.prompt.version"));
        Assert.Equal(release.ActiveChatDeployment, root.GetTagItem("paymentops.chat.deployment"));
        var search = Assert.Single(trace, a => a.OperationName == "knowledge.search");
        Assert.Equal(release.KnowledgeIndex, search.GetTagItem("paymentops.search.index"));
    }

    [Fact]
    public async Task Screened_tool_text_names_its_source_and_summarises_its_verdicts()
    {
        var (_, trace, _) = await RunAsync(Turns["assessed"]);

        var screening = Assert.Single(trace, a => a.OperationName == "screen.tool_text" && Equals(a.GetTagItem("paymentops.screen.source"), "knowledge_section"));
        Assert.Equal("clean", screening.GetTagItem("paymentops.screen.verdict"));
        Assert.True(screening.GetTagItem("paymentops.screen.values") is int values && values > 0);
    }

    [Theory]
    [InlineData(new[] { ScreenVerdict.Clean, ScreenVerdict.Clean }, "clean")]
    [InlineData(new[] { ScreenVerdict.Attack }, "attack")]
    [InlineData(new[] { ScreenVerdict.Clean, ScreenVerdict.Attack }, "mixed")]
    [InlineData(new[] { ScreenVerdict.Harmful, ScreenVerdict.Unavailable }, "mixed")]
    public void Several_screened_values_have_one_verdict_summary(ScreenVerdict[] verdicts, string summary) =>
        Assert.Equal(summary, ToolGuard.VerdictSummary(verdicts));

    // Screens run inside the screening span, so a cache hit is attributed to the span that served it.
    [Fact]
    public void Cache_hits_are_attributed_to_the_current_screening_span()
    {
        using (var screening = AgentTelemetry.Source.StartActivity("screen.tool_text")!)
        {
            TurnUsage.RecordCacheHit("harm", 2);
            TurnUsage.RecordCacheHit("prompt_shields");
            TurnUsage.RecordCacheHit("harm");

            Assert.Equal(3, screening.GetTagItem("paymentops.cache.harm.hits"));
            Assert.Equal(1, screening.GetTagItem("paymentops.cache.prompt_shields.hits"));
        }
    }

    [Fact]
    public async Task An_unexpected_failure_is_traced_as_an_error_and_still_recorded()
    {
        var turn = new Turn("error", Questions: new ThrowingQuestionScreen());

        var run = RunAsync(turn);

        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        List<Activity> roots;
        lock (_activities)
        {
            roots = [.. _activities.Where(a => a.OperationName == "investigation" && Equals(a.GetTagItem("paymentops.outcome"), "error"))];
        }

        Assert.Contains(roots, a => a.Status == ActivityStatusCode.Error);
    }

    private sealed class ThrowingQuestionScreen : IQuestionScreen
    {
        public Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken) => throw new InvalidOperationException("screen broke");
    }

    [Theory]
    [InlineData("assessed")]
    [InlineData("question blocked")]
    [InlineData("model unavailable")]
    [InlineData("cancelled")]
    public async Task No_telemetry_carries_the_question_answer_tool_output_or_user(string row)
    {
        var (_, trace, logger) = await RunAsync(Turns[row]);

        string[] sentinels = [QuestionSentinel, AnswerSentinel, ToolSentinel, UserSentinel];
        var tagValues = trace.SelectMany(a => a.TagObjects.Select(t => t.Value?.ToString() ?? string.Empty)
            .Concat(a.Events.SelectMany(e => e.Tags.Select(t => t.Value?.ToString() ?? string.Empty))));
        var measuredTags = _measurements.SelectMany(m => m.Tags.Select(t => t.Value?.ToString() ?? string.Empty));
        var logged = logger.Entries.Select(e => e.Message);

        foreach (var text in tagValues.Concat(measuredTags).Concat(logged))
        {
            Assert.DoesNotContain(sentinels, s => text.Contains(s, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Blocked_questions_are_counted_by_kind()
    {
        await RunAsync(Turns["question blocked"]);

        Assert.Contains(Measured("paymentops.screen.blocks"), m => m.Tags.Contains(new("kind", "question")));
    }

    [Fact]
    public async Task A_busy_click_is_a_rejection_not_an_investigation()
    {
        var gate = new InFlightGate(new BlockingService());
        var first = gate.InvestigateAsync("q", null, CancellationToken.None);

        var busy = await gate.InvestigateAsync("q", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.Busy, Assert.IsType<AssessmentResult.NotAssessed>(busy).Reason);
        Assert.Contains(Measured("paymentops.investigations.rejected"), m => m.Tags.SequenceEqual([new("reason", "busy")]));
        Assert.DoesNotContain(Measured("paymentops.investigations"), _ => true);
        lock (_activities)
        {
            Assert.DoesNotContain(_activities, a => a.OperationName == "investigation");
        }

        BlockingService.Release.SetResult();
        await first;
    }

    private sealed class BlockingService : IInvestigationService
    {
        public static TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConversationStatus Conversation => new(0, 0, false);

        public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
        {
            await Release.Task;
            return new AssessmentResult.NotAssessed(NotAssessedReason.Cancelled);
        }

        public void StartNew()
        {
        }

        public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
