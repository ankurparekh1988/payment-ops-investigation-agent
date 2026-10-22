using PaymentOps.Data.ContractTests.Contracts;
using PaymentOps.Data.Demo;
using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.ContractTests.Demo;

// The demo adapters run the same contracts a real adapter would, over the contract records loaded as a dataset.
public sealed class DemoMerchantDataTests : MerchantDataContract
{
    protected override IMerchantData Create(ContractData data) => new DemoMerchantData(DemoFixture.Dataset(data));
}

public sealed class DemoSettlementDataTests : SettlementDataContract
{
    protected override ISettlementData Create(ContractData data) => new DemoSettlementData(DemoFixture.Dataset(data));
}

public sealed class DemoProcessorDataTests : ProcessorDataContract
{
    protected override IProcessorData Create(ContractData data) => new DemoProcessorData(DemoFixture.Dataset(data));
}

public sealed class DemoErrorEventDataTests : ErrorEventDataContract
{
    protected override IErrorEventData Create(ContractData data) => new DemoErrorEventData(DemoFixture.Dataset(data));
}

// The contract records are written as collection expressions, which are already read-only; these check the
// lists the loader builds from JSON and the ones a caller of Checked passes in.
public sealed class DemoSnapshotTests
{
    [Fact]
    public async Task The_shipped_dataset_cannot_be_changed_through_its_ports()
    {
        var dataset = DemoDatasetLoader.LoadDefault();
        var settlement = new DemoSettlementData(dataset);
        var file = (await settlement.GetFilesAsync("fabrikam-pay", new(2026, 11, 2), new(2026, 11, 3), CancellationToken.None))[0];
        var payout = (await settlement.GetPayoutsAsync("M-1042", new(2026, 11, 1), new(2026, 12, 1), CancellationToken.None))[0];

        ReadOnlyResults.AssertReadOnly((await new DemoMerchantData(dataset).FindAsync("M-1042", CancellationToken.None))!.ProcessorIds);
        ReadOnlyResults.AssertReadOnly(file.Lines);
        ReadOnlyResults.AssertReadOnly(file.Lines[0].PaymentIds);
        ReadOnlyResults.AssertReadOnly(payout.SourceFileIds);
    }

    [Fact]
    public async Task A_checked_dataset_no_longer_shares_the_lists_it_was_given()
    {
        var data = new ContractData();
        var processorIds = new List<string>(data.Merchants[0].ProcessorIds);
        var dataset = DemoFixture.Dataset(data) with { Merchants = [data.Merchants[0] with { ProcessorIds = processorIds }, .. data.Merchants.Skip(1)] };

        var merchant = await new DemoMerchantData(DemoDatasetLoader.Checked(dataset)).FindAsync("M-1", CancellationToken.None);
        processorIds.Clear();

        Assert.Equal(["p-a", "p-b"], merchant!.ProcessorIds);
        ReadOnlyResults.AssertReadOnly(merchant.ProcessorIds);
    }
}

internal static class DemoFixture
{
    // Through the integrity check, so the contract records are also proof that a consistent dataset passes it.
    public static DemoDataset Dataset(ContractData data) => DemoDatasetLoader.Checked(new DemoDataset(
        ContractData.AsOf,
        data.Processors,
        [.. data.ClosureDays.Select(day => new ClosureDay(day.Date, day.ClosureReason!))],
        data.Merchants,
        data.Payments,
        data.Files,
        data.Payouts,
        data.Incidents,
        data.ErrorEvents));
}

public sealed class InMemoryIncidentStoreTests : IncidentStoreContract
{
    protected override IIncidentStore Create(int maxTickets) => new InMemoryIncidentStore(Time, maxTickets);
}
