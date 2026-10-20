using PaymentOps.Domain.ErrorEvents;

using static PaymentOps.Data.ContractTests.Contracts.ContractData;

namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>What every <see cref="IErrorEventData"/> must do. An implementation's tests inherit this class.</summary>
public abstract class ErrorEventDataContract
{
    private static readonly DateTimeOffset Start = Utc(2026, 11, 1, 0, 0);
    private static readonly DateTimeOffset End = Utc(2026, 11, 7, 0, 0);

    protected abstract IErrorEventData Create(ContractData data);

    private IErrorEventData Port => Create(new ContractData());

    [Fact]
    public async Task A_processors_events_come_in_time_order_then_id()
    {
        var events = await Port.GetEventsAsync(new("p-a", null, Start, End), CancellationToken.None);

        Assert.Equal(["EV-1", "EV-4", "EV-2"], events.Select(e => e.Id));
    }

    [Fact]
    public async Task A_merchants_events_span_processors()
    {
        var events = await Port.GetEventsAsync(new(null, "M-1", Start, End), CancellationToken.None);

        Assert.Equal(["EV-1", "EV-3"], events.Select(e => e.Id));
    }

    [Fact]
    public async Task Processor_and_merchant_together_narrow_the_events()
    {
        var events = await Port.GetEventsAsync(new("p-a", "M-1", Start, End), CancellationToken.None);

        Assert.Equal(["EV-1"], events.Select(e => e.Id));
    }

    [Fact]
    public async Task The_range_is_half_open()
    {
        var events = await Port.GetEventsAsync(new("p-a", null, Utc(2026, 11, 2, 15, 0), Utc(2026, 11, 3, 0, 0)), CancellationToken.None);

        Assert.Equal(["EV-1", "EV-4"], events.Select(e => e.Id));
    }

    [Fact]
    public async Task A_query_needs_a_processor_or_a_merchant() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetEventsAsync(new(null, null, Start, End), CancellationToken.None));

    [Fact]
    public async Task From_must_be_before_to() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetEventsAsync(new("p-a", null, End, End), CancellationToken.None));

    [Fact]
    public async Task Unknown_or_differently_cased_ids_find_nothing()
    {
        Assert.Empty(await Port.GetEventsAsync(new("P-A", null, Start, End), CancellationToken.None));
        Assert.Empty(await Port.GetEventsAsync(new(null, "m-1", Start, End), CancellationToken.None));
    }

    [Fact]
    public async Task There_is_no_maximum_span() =>
        Assert.Equal(["EV-1", "EV-3"], (await Port.GetEventsAsync(new(null, "M-1", Utc(2025, 1, 1, 0, 0), Utc(2027, 1, 1, 0, 0)), CancellationToken.None)).Select(e => e.Id));

    [Fact]
    public async Task A_cancelled_token_throws() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port.GetEventsAsync(new("p-a", null, Start, End), Cancelled()));
}
