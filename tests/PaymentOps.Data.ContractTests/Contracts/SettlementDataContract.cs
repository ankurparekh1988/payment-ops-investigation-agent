using PaymentOps.Domain.Settlement;

using static PaymentOps.Data.ContractTests.Contracts.ContractData;

namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>What every <see cref="ISettlementData"/> must do. An implementation's tests inherit this class.</summary>
public abstract class SettlementDataContract
{
    private static readonly DateOnly Nov2 = new(2026, 11, 2);
    private static readonly DateOnly Nov3 = new(2026, 11, 3);
    private static readonly DateOnly Nov4 = new(2026, 11, 4);

    protected abstract ISettlementData Create(ContractData data);

    private ISettlementData Port => Create(new ContractData());

    [Fact]
    public async Task Payments_are_the_merchants_own_in_capture_order_then_id()
    {
        var payments = await Port.GetPaymentsAsync("M-1", Utc(2026, 11, 1, 0, 0), Utc(2026, 11, 7, 0, 0), CancellationToken.None);

        Assert.Equal(["PAY-1", "PAY-2", "PAY-3", "PAY-6", "PAY-5"], payments.Select(p => p.Id));
    }

    [Fact]
    public async Task Results_and_the_lists_inside_them_cannot_be_changed_by_the_caller()
    {
        var files = await Port.GetFilesAsync("p-a", Nov2, Nov4, CancellationToken.None);
        var payouts = await Port.GetPayoutsAsync("M-1", Nov2, Nov4, CancellationToken.None);

        ReadOnlyResults.AssertReadOnly(await Port.GetPaymentsAsync("M-1", Utc(2026, 11, 1, 0, 0), Utc(2026, 11, 7, 0, 0), CancellationToken.None));
        ReadOnlyResults.AssertReadOnly(await Port.GetCalendarAsync(Nov2, Nov4, CancellationToken.None));
        ReadOnlyResults.AssertReadOnly(files);
        ReadOnlyResults.AssertReadOnly(files[0].Lines);
        ReadOnlyResults.AssertReadOnly(files[0].Lines[0].PaymentIds);
        ReadOnlyResults.AssertReadOnly(payouts);
        ReadOnlyResults.AssertReadOnly(payouts[0].SourceFileIds);
    }

    [Fact]
    public async Task Payment_range_is_half_open()
    {
        var payments = await Port.GetPaymentsAsync("M-1", Utc(2026, 11, 2, 10, 0), Utc(2026, 11, 3, 0, 0), CancellationToken.None);

        Assert.Equal(["PAY-1"], payments.Select(p => p.Id));
    }

    [Fact]
    public async Task Files_are_the_processors_own_by_batch_date_with_a_half_open_range()
    {
        var files = await Port.GetFilesAsync("p-a", Nov2, Nov4, CancellationToken.None);
        var first = await Port.GetFilesAsync("p-a", Nov2, Nov3, CancellationToken.None);

        Assert.Equal(["SF-A1", "SF-A2"], files.Select(f => f.Id));
        Assert.Equal(["SF-A1"], first.Select(f => f.Id));
        Assert.Equal(["M-1", "M-2"], files[0].Lines.Select(l => l.MerchantId));
    }

    [Fact]
    public async Task Payouts_are_the_merchants_own_by_batch_date_then_id_with_their_provenance()
    {
        // PO-5's batch date is the range end, so it's excluded.
        var payouts = await Port.GetPayoutsAsync("M-1", Nov2, Nov4, CancellationToken.None);

        Assert.Equal(["PO-1", "PO-2", "PO-3"], payouts.Select(p => p.Id));
        Assert.Equal(["SF-B1"], payouts[2].SourceFileIds);
        Assert.Equal(new DateOnly(2026, 11, 9), payouts[2].ScheduledFor);
        Assert.Equal(PayoutStatus.Scheduled, payouts[2].Status);
    }

    [Fact]
    public async Task Every_date_in_range_is_answered()
    {
        var days = await Port.GetCalendarAsync(Nov2, new DateOnly(2026, 11, 9), CancellationToken.None);

        Assert.Equal(7, days.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(Nov2.AddDays), days.Select(d => d.Date));
        Assert.Equal([true, true, false, true, true, false, false], days.Select(d => d.IsSettlementDay));
        Assert.Equal("Settlement system closure", days.Single(d => d.Date == ClosureDate).ClosureReason);
        Assert.All(days.Where(d => d.Date != ClosureDate), d => Assert.Null(d.ClosureReason));
    }

    [Fact]
    public async Task The_calendar_answers_dates_beyond_any_listed_closure()
    {
        var days = await Port.GetCalendarAsync(new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 14), CancellationToken.None);

        Assert.Equal([true, true, true, true, true, false, false], days.Select(d => d.IsSettlementDay));
    }

    [Fact]
    public async Task Unknown_or_differently_cased_ids_find_nothing()
    {
        Assert.Empty(await Port.GetPaymentsAsync("m-1", Utc(2026, 11, 1, 0, 0), Utc(2026, 11, 7, 0, 0), CancellationToken.None));
        Assert.Empty(await Port.GetFilesAsync("p-z", Nov2, Nov4, CancellationToken.None));
        Assert.Empty(await Port.GetPayoutsAsync("M-404", Nov2, Nov4, CancellationToken.None));
    }

    [Fact]
    public async Task From_must_be_before_to()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetPaymentsAsync("M-1", Utc(2026, 11, 3, 0, 0), Utc(2026, 11, 3, 0, 0), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetFilesAsync("p-a", Nov3, Nov2, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetPayoutsAsync("M-1", Nov3, Nov3, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Port.GetCalendarAsync(Nov3, Nov2, CancellationToken.None));
    }

    [Fact]
    public async Task There_is_no_maximum_span()
    {
        var payments = await Port.GetPaymentsAsync("M-1", Utc(2026, 1, 1, 0, 0), Utc(2027, 2, 5, 0, 0), CancellationToken.None);
        var days = await Port.GetCalendarAsync(new DateOnly(2026, 1, 1), new DateOnly(2027, 2, 5), CancellationToken.None);

        Assert.Equal(5, payments.Count);
        Assert.Equal(400, days.Count);
    }

    [Fact]
    public async Task A_cancelled_token_throws()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port.GetPaymentsAsync("M-1", Utc(2026, 11, 1, 0, 0), Utc(2026, 11, 7, 0, 0), Cancelled()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port.GetCalendarAsync(Nov2, Nov4, Cancelled()));
    }
}
