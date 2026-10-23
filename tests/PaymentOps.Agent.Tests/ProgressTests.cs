using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Tools;
using PaymentOps.Tools.ErrorEvents;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>What the person watching sees while the agent works: server-written events, in call order.</summary>
public sealed class ProgressTests
{
    private const string Submit = ToolNames.SubmitAssessment;

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Respond(ChatResponse response) => _ => response;

    [Fact]
    public async Task Events_follow_the_calls_in_order()
    {
        using var fixture = new AgentFixture();
        var progress = new RecordingProgress();
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Call(ToolNames.SettlementBatches, new { merchant_id = "M-1042", start_date = "2026-11-09", end_date = "2026-11-10" })),
            Respond(Call(Submit, Submission())));

        await fixture.Runner(model).InvestigateAsync("q", [], progress, Guid.NewGuid(), CancellationToken.None);

        Assert.Collection(
            progress.Events,
            e => Assert.Equal(new InvestigationProgress.ToolStarted(ToolNames.MerchantProfile, "Merchant profile · M-1042"), e),
            e => Assert.Equal(new InvestigationProgress.ToolFinished(ToolNames.MerchantProfile, "Merchant profile · M-1042", true, 3, null), e),
            e => Assert.Equal(new InvestigationProgress.ToolStarted(ToolNames.SettlementBatches, "Settlement batches · M-1042 · 9 Nov–10 Nov"), e),
            e => Assert.True(e is InvestigationProgress.ToolFinished { Tool: ToolNames.SettlementBatches, Succeeded: true, Records: > 0 }),
            e => Assert.IsType<InvestigationProgress.Submitting>(e));
    }

    [Fact]
    public async Task Labels_come_from_parsed_arguments_not_model_text()
    {
        using var fixture = new AgentFixture();
        var progress = new RecordingProgress();
        var injected = "<script>alert(1)</script>" + new string('x', 275);
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.KnowledgeSearch, new { query = injected })),
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = injected })),
            Respond(Call(Submit, Submission())));

        await fixture.Runner(model).InvestigateAsync("q", [], progress, Guid.NewGuid(), CancellationToken.None);

        var labels = progress.Events.OfType<InvestigationProgress.ToolStarted>().Select(e => e.Label).ToList();
        Assert.Equal(["Knowledge search", "Merchant profile"], labels);
        Assert.All(progress.Events.OfType<InvestigationProgress.ToolFinished>(), e => Assert.DoesNotContain("<", e.Label, StringComparison.Ordinal));
    }

    [Fact]
    public void A_label_never_exceeds_eighty_characters()
    {
        var id = new string('a', 64);

        Assert.True(ProgressLabels.For(ToolNames.ErrorEvents, new ErrorEventsRequest(id, id, "2026-11-01", "2026-11-10")).Length <= 80);
    }

    [Fact]
    public async Task A_direct_submission_reports_submitting_first()
    {
        using var fixture = new AgentFixture();
        var progress = new RecordingProgress();
        var refusal = new
        {
            outcome = "refused",
            summary = new { text = "Out of scope.", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() },
            claims = Array.Empty<object>(),
            root_cause = new { text = "", evidence_ids = Array.Empty<string>(), citation_ids = Array.Empty<string>() },
            confidence = "low",
            next_steps = Array.Empty<object>(),
            proposed_ticket = (object?)null,
        };
        var model = new ScriptedChatClient(Respond(Call(Submit, refusal)));

        await fixture.Runner(model).InvestigateAsync("What's the weather?", [], progress, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<InvestigationProgress.Submitting>(progress.Events[0]);
    }

    [Fact]
    public async Task A_rejected_submission_reports_a_correction()
    {
        using var fixture = new AgentFixture();
        var progress = new RecordingProgress();
        var model = new ScriptedChatClient(
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))),
            Respond(Call(Submit, Submission(claimEvidence: ["payout:PO-404"]))));

        await fixture.Runner(model).InvestigateAsync("q", [], progress, Guid.NewGuid(), CancellationToken.None);

        Assert.Contains(progress.Events, e => e is InvestigationProgress.CorrectionRequested);
    }

    [Fact]
    public async Task The_call_limit_reports_limit_reached()
    {
        using var fixture = new AgentFixture();
        var progress = new RecordingProgress();
        var calls = Enumerable.Range(0, 11)
            .Select(i => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => Call(ToolNames.MerchantProfile, new { merchant_id = $"M-{9000 + i}" })));
        var model = new ScriptedChatClient([.. calls, Respond(Call(Submit, Submission()))]);

        await fixture.Runner(model).InvestigateAsync("q", [], progress, Guid.NewGuid(), CancellationToken.None);

        Assert.Single(progress.Events.OfType<InvestigationProgress.LimitReached>());
    }

    [Fact]
    public async Task Cancelling_returns_cancelled()
    {
        using var fixture = new AgentFixture();
        using var model = new WaitingChatClient();
        using var cancel = new CancellationTokenSource();

        var running = fixture.Runner(model).InvestigateAsync("q", [], null, Guid.NewGuid(), cancel.Token);
        await model.Started.Task;
        await cancel.CancelAsync();

        Assert.Equal(NotAssessedReason.Cancelled, Assert.IsType<AssessmentResult.NotAssessed>(await running).Reason);
    }

    [Fact]
    public async Task Every_result_carries_its_investigation_id()
    {
        using var fixture = new AgentFixture();
        var logger = new RecordingLogger<InvestigationAgentRunner>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var a = await fixture.Runner(new ScriptedChatClient(Respond(Call(Submit, Submission()))), logger: logger).InvestigateAsync("q", [], null, first, CancellationToken.None);
        var b = await fixture.Runner(new ScriptedChatClient(Respond(Call(Submit, Submission()))), logger: logger).InvestigateAsync("", [], null, second, CancellationToken.None);

        Assert.Equal(first, a.InvestigationId);
        Assert.Equal(second, b.InvestigationId);
        Assert.Contains(first.ToString(), logger.Entries[0].Message, StringComparison.Ordinal);
    }

    /// <summary>Appends as it's told, on the caller's thread, so the order is the order of the calls.</summary>
    internal sealed class RecordingProgress : IProgress<InvestigationProgress>
    {
        public List<InvestigationProgress> Events { get; } = [];

        public void Report(InvestigationProgress value) => Events.Add(value);
    }

    /// <summary>A model that never answers, so only a timeout or cancellation can end the call.</summary>
    internal sealed class WaitingChatClient : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
