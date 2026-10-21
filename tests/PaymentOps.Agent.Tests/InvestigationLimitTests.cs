using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>The runner's limits end to end: the turn timeout, the call limit, and the caller's own cancellation.</summary>
public sealed class InvestigationLimitTests
{
    [Fact]
    public async Task The_turn_timeout_ends_the_investigation_at_its_limit()
    {
        using var fixture = new AgentFixture();
        var time = new FakeTimeProvider();
        using var model = new WaitingChatClient();

        var running = fixture.Runner(model, time: time).InvestigateAsync("q", CancellationToken.None);
        await model.Started.Task;
        time.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(NotAssessedReason.LimitReached, Assert.IsType<AssessmentResult.NotAssessed>(await running).Reason);
    }

    // The 11th call stops the loop; the one allowed request then submits, and the server marks it incomplete.
    [Fact]
    public async Task Hitting_the_call_limit_still_allows_one_submission_and_makes_it_incomplete()
    {
        using var fixture = new AgentFixture();
        var calls = Enumerable.Range(0, 11)
            .Select(i => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => Call(ToolNames.MerchantProfile, new { merchant_id = i == 0 ? "M-1042" : $"M-{9000 + i}" })));
        var model = new ScriptedChatClient([.. calls, _ => Call(ToolNames.SubmitAssessment, Submission())]);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None));

        Assert.Equal(AssessmentOutcome.Incomplete, assessed.Outcome);
        Assert.Equal(Confidence.Low, assessed.Confidence);
        Assert.Contains("The investigation stopped at its step limit.", assessed.Gaps);
        Assert.Equal([ToolNames.SubmitAssessment], model.Requests[^1].Tools);
    }

    [Fact]
    public async Task The_callers_cancellation_isnt_turned_into_a_result()
    {
        using var fixture = new AgentFixture();
        using var model = new WaitingChatClient();
        using var cancel = new CancellationTokenSource();

        var running = fixture.Runner(model).InvestigateAsync("q", cancel.Token);
        await model.Started.Task;
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    // Service messages can echo the request; only the failure's type and codes are logged.
    [Fact]
    public async Task A_model_failure_is_logged_without_its_message()
    {
        using var fixture = new AgentFixture();
        var logger = new RecordingLogger<InvestigationAgentRunner>();
        var model = new ScriptedChatClient(_ => throw new HttpRequestException("echo: Why were M-1042's payouts delayed?"));

        await fixture.Runner(model, logger: logger).InvestigateAsync("Why were M-1042's payouts delayed?", CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Contains("HttpRequestException", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("M-1042", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>A model that never answers, so only a timeout or cancellation can end the call.</summary>
    private sealed class WaitingChatClient : IChatClient
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
