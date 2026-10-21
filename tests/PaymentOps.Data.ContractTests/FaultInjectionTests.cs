using System.Diagnostics;

using PaymentOps.Data.ContractTests.Contracts;
using PaymentOps.Data.ContractTests.Demo;
using PaymentOps.Data.Demo;
using PaymentOps.Data.Demo.Faults;
using PaymentOps.Domain.Data;

namespace PaymentOps.Data.ContractTests;

public sealed class FaultInjectionTests
{
    private static readonly DateOnly Nov2 = new(2026, 11, 2);
    private static readonly DateOnly Nov4 = new(2026, 11, 4);

    [Fact]
    public async Task Delay_returns_the_real_result_after_the_delay()
    {
        var watch = Stopwatch.StartNew();

        var files = await Settlement(FaultMode.Delay, TimeSpan.FromMilliseconds(150)).GetFilesAsync("p-a", Nov2, Nov4, CancellationToken.None);

        Assert.Equal(2, files.Count);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(140), $"Returned after {watch.Elapsed}.");
    }

    [Fact]
    public async Task Hang_waits_until_the_token_is_cancelled()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Settlement(FaultMode.Hang).GetFilesAsync("p-a", Nov2, Nov4, cancel.Token));
    }

    [Fact]
    public async Task Empty_returns_no_items_or_nothing_found()
    {
        var merchants = new FaultInjectingMerchantData(new DemoMerchantData(DemoFixture.Dataset(new ContractData())), Faults(FaultMode.Empty));

        Assert.Empty(await Settlement(FaultMode.Empty).GetFilesAsync("p-a", Nov2, Nov4, CancellationToken.None));
        Assert.Null(await merchants.FindAsync("M-1", CancellationToken.None));
    }

    [Fact]
    public async Task Unavailable_throws_a_transient_failure_naming_the_port()
    {
        var failure = await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => Settlement(FaultMode.Unavailable).GetFilesAsync("p-a", Nov2, Nov4, CancellationToken.None));

        Assert.True(failure.IsTransient);
        Assert.Equal("Settlement", failure.DataSource);
    }

    [Fact]
    public async Task Argument_errors_still_come_from_the_real_adapter()
    {
        // A fault stands in for the data source being unwell, not for the caller asking a bad question.
        await Assert.ThrowsAsync<ArgumentException>(() => Settlement(FaultMode.Empty).GetFilesAsync("p-a", Nov4, Nov2, CancellationToken.None));
    }

    private static FaultInjectingSettlementData Settlement(FaultMode mode, TimeSpan? delay = null) =>
        new(new DemoSettlementData(DemoFixture.Dataset(new ContractData())), Faults(mode, delay));

    private static FaultInjector Faults(FaultMode mode, TimeSpan? delay = null) =>
        new(new FaultOptions(mode, delay ?? TimeSpan.FromMilliseconds(10)), "Settlement");
}
