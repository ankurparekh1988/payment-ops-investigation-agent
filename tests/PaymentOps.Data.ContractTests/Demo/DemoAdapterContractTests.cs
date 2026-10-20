using PaymentOps.Data.ContractTests.Contracts;
using PaymentOps.Data.Demo;
using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.ErrorEvents;
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
