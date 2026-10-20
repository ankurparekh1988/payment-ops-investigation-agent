using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Tests;

public sealed class InFlightAskGateTests
{
    [Fact]
    public async Task A_second_ask_while_one_is_running_is_busy_and_never_reaches_the_service()
    {
        var inner = new BlockingService();
        var gate = new InFlightAskGate(inner);

        var first = gate.AskAsync("one", CancellationToken.None);
        var second = await gate.AskAsync("two", CancellationToken.None);

        Assert.Equal(NotAnsweredReason.Busy, Assert.IsType<InvestigationResult.NotAnswered>(second).Reason);
        Assert.Equal(1, inner.Asks);

        inner.Release.SetResult(new InvestigationResult.NotAnswered(NotAnsweredReason.NoEvidence));
        await first;
        inner.Release = new();
        _ = gate.AskAsync("three", CancellationToken.None);
        Assert.Equal(2, inner.Asks);
    }

    [Fact]
    public async Task The_gate_is_released_when_the_ask_fails()
    {
        var inner = new BlockingService();
        var gate = new InFlightAskGate(inner);

        var first = gate.AskAsync("one", CancellationToken.None);
        inner.Release.SetException(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);

        inner.Release = new();
        _ = gate.AskAsync("two", CancellationToken.None);
        Assert.Equal(2, inner.Asks);
    }

    [Fact]
    public async Task Sources_pass_straight_through()
    {
        var gate = new InFlightAskGate(new BlockingService());

        Assert.IsType<SourceResult.NotFound>(await gate.GetSourceAsync("key", CancellationToken.None));
    }

    private sealed class BlockingService : IInvestigationService
    {
        public TaskCompletionSource<InvestigationResult> Release { get; set; } = new();

        public int Asks { get; private set; }

        public Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken)
        {
            Asks++;
            return Release.Task;
        }

        public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
            Task.FromResult<SourceResult>(new SourceResult.NotFound());
    }
}
