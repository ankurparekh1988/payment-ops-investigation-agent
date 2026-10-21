using Microsoft.Extensions.Options;

using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;
using PaymentOps.Tools.ErrorEvents;
using PaymentOps.Tools.Merchants;
using PaymentOps.Tools.Settlement;

using static PaymentOps.Tools.Tests.ToolFixture;

namespace PaymentOps.Tools.Tests;

public sealed class ToolEdgeCaseTests
{
    private static readonly ProcessorConfiguration LosAngeles = new(new TimeOnly(17, 0), "America/Los_Angeles", new TimeOnly(2, 0), TimeSpan.FromHours(2), 0);

    // 00:30 UTC on the 10th is 16:30 on the 9th in Los Angeles: the payment's local day is before the range starts.
    [Fact]
    public async Task A_capture_whose_local_day_is_before_the_range_still_gets_its_batch()
    {
        var ports = new FakePorts(new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero));
        ports.Merchants.Add(new Merchant("M-1", "Merchant", ["p-la"], null));
        ports.Processors.Add(new Processor("p-la", "West Coast Pay", LosAngeles));
        ports.Payments.Add(new Payment("PAY-1", "M-1", "p-la", 10m, "USD", new DateTimeOffset(2026, 11, 10, 0, 30, 0, TimeSpan.Zero), null, null));
        var tool = new SettlementBatchesTool(ports, ports, ports, ports, Options.Create(new ToolOptions()));

        var ok = Ok(await tool.RunAsync(new SettlementBatchesRequest("M-1", "2026-11-10", "2026-11-10"), TestUser.Reader, CancellationToken.None));

        Assert.Contains("fact:expected-batch:PAY-1", Ids(ok));
        Assert.Contains("\"date\":\"2026-11-09\"", ToolJson.Serialize(ok), StringComparison.Ordinal);
    }

    // "A file we received but rejected is not missing; it needs fixing and reprocessing." (missing-settlement-batch)
    [Fact]
    public async Task A_batch_whose_only_file_was_rejected_isnt_missing()
    {
        var ports = new FakePorts(new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero));
        ports.Merchants.Add(new Merchant("M-1", "Merchant", ["p-la"], null));
        ports.Processors.Add(new Processor("p-la", "West Coast Pay", LosAngeles));
        ports.Payments.Add(new Payment("PAY-1", "M-1", "p-la", 10m, "USD", new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero), null, null));
        ports.Files.Add(new SettlementFile("SF-1", "p-la", new DateOnly(2026, 11, 10), new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero), SettlementFileStatus.Rejected, [new SettlementLine("M-1", ["PAY-1"], 10m, "USD")]));
        var tool = new SettlementBatchesTool(ports, ports, ports, ports, Options.Create(new ToolOptions()));

        var ok = Ok(await tool.RunAsync(new SettlementBatchesRequest("M-1", "2026-11-10", "2026-11-10"), TestUser.Reader, CancellationToken.None));

        Assert.DoesNotContain(ok.Evidence, e => e.Id.StartsWith("fact:missing-file:", StringComparison.Ordinal));
        Assert.Contains("\"status\":\"rejected\"", ToolJson.Serialize(ok), StringComparison.Ordinal);
    }

    // Evidence is what a tool actually read; a routed processor with no record isn't one.
    [Fact]
    public async Task A_routed_processor_with_no_record_isnt_registered_as_evidence()
    {
        var ports = new FakePorts(new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero));
        ports.Merchants.Add(new Merchant("M-1", "Merchant", ["p-gone"], null));
        var tool = new MerchantProfileTool(ports, ports, Options.Create(new ToolOptions()));

        var ok = Ok(await tool.RunAsync(new MerchantProfileRequest("M-1"), TestUser.Reader, CancellationToken.None));

        Assert.DoesNotContain("processor:p-gone", Ids(ok));
    }

    // Models often send unused optional parameters as empty strings.
    [Fact]
    public async Task A_blank_optional_id_counts_as_not_given()
    {
        using var tools = new ToolFixture();

        Ok(await tools.Tool<ErrorEventsTool>().RunAsync(new ErrorEventsRequest("", "M-1042", "2026-11-04", "2026-11-04"), TestUser.Reader, CancellationToken.None));
    }
}
