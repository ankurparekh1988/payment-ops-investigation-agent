using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;

namespace PaymentOps.Agent.Tests;

public sealed class InFlightGateTests
{
    [Fact]
    public async Task A_second_investigation_while_one_is_running_is_busy_and_never_reaches_the_service()
    {
        var inner = new BlockingService();
        var gate = new InFlightGate(inner);

        var first = gate.InvestigateAsync("one", null, CancellationToken.None);
        var second = await gate.InvestigateAsync("two", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.Busy, Assert.IsType<AssessmentResult.NotAssessed>(second).Reason);
        Assert.Equal(1, inner.Investigations);

        inner.Release.SetResult(new AssessmentResult.NotAssessed(NotAssessedReason.LimitReached));
        await first;
        inner.Release = new();
        _ = gate.InvestigateAsync("three", null, CancellationToken.None);
        Assert.Equal(2, inner.Investigations);
    }

    [Fact]
    public async Task The_gate_is_released_when_the_investigation_fails()
    {
        var inner = new BlockingService();
        var gate = new InFlightGate(inner);

        var first = gate.InvestigateAsync("one", null, CancellationToken.None);
        inner.Release.SetException(new InvalidOperationException());
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);

        inner.Release = new();
        _ = gate.InvestigateAsync("two", null, CancellationToken.None);
        Assert.Equal(2, inner.Investigations);
    }

    [Fact]
    public async Task Sources_pass_straight_through()
    {
        var gate = new InFlightGate(new BlockingService());

        Assert.IsType<SourceResult.NotFound>(await gate.GetSourceAsync("key", CancellationToken.None));
    }

    private sealed class BlockingService : IInvestigationService
    {
        public TaskCompletionSource<AssessmentResult> Release { get; set; } = new();

        public int Investigations { get; private set; }

        public Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
        {
            Investigations++;
            return Release.Task;
        }

        public ConversationStatus Conversation { get; } = new(0, 0, false);

        public void StartNew()
        {
        }

        public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
            Task.FromResult<SourceResult>(new SourceResult.NotFound());
    }
}
