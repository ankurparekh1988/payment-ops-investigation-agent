using PaymentOps.Tools.Settlement;

using static PaymentOps.Tools.Tests.ToolFixture;

namespace PaymentOps.Tools.Tests;

public sealed class SettlementBatchesToolTests
{
    private static Task<ToolResult> Run(ToolFixture tools, string merchant, string start, string end) =>
        tools.Tool<SettlementBatchesTool>().RunAsync(new SettlementBatchesRequest(merchant, start, end), TestUser.Reader, CancellationToken.None);

    // S1: the Fabrikam batch for Monday 9 November never arrived, and the payments that should be in it are the evidence.
    [Fact]
    public async Task A_due_batch_with_expected_payments_and_no_file_is_reported_missing()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await Run(tools, "M-1042", "2026-11-09", "2026-11-10"));

        var missing = Fact(ok, "fact:missing-file:fabrikam-pay:2026-11-09");
        Assert.Contains("processor:fabrikam-pay", missing.SourceIds);
        Assert.Contains(missing.SourceIds, id => id.StartsWith("payment:PAY-1042-", StringComparison.Ordinal));
        Assert.All(missing.SourceIds, id => Assert.Contains(id, Ids(ok)));
        Assert.DoesNotContain(ok.Evidence, e => e.Id == "fact:missing-file:northwind-acquiring:2026-11-09");
        Assert.Contains("file:SF-NW-1109", Ids(ok));
        Assert.Contains("payout:PO-1042-NW-1109", Ids(ok));
        Assert.DoesNotContain(ok.Evidence, e => e.Id == "fact:overdue:PO-1042-NW-1109");
    }

    // S2: captured five minutes after Northwind's London cut-off, then a closure day and a weekend.
    [Fact]
    public async Task A_payment_captured_after_the_cut_off_shows_its_expected_batch_and_why()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await Run(tools, "M-6230", "2026-11-05", "2026-11-10"));
        var payment = Json(ok)["processors"]!.AsArray().SelectMany(p => p!["batches"]!["items"]!.AsArray()).SelectMany(b => b!["payments"]!["items"]!.AsArray())
            .Single(p => (string?)p!["id"] == "PAY-6230-04")!;

        Assert.True((bool)payment["after_cut_off"]!);
        Assert.Equal("2026-11-05T17:05:03", (string?)payment["local_capture_time"]);
        Assert.Equal("2026-11-09", (string?)payment["expected_batch"]!["date"]);
        var expected = Fact(ok, "fact:expected-batch:PAY-6230-04");
        Assert.Equal(["payment:PAY-6230-04", "processor:northwind-acquiring", "calendar:2026-11-06", "calendar:2026-11-07", "calendar:2026-11-08"], expected.SourceIds);
        Assert.Contains("fact:non-settlement-day:2026-11-06", Ids(ok));
    }

    // S4 and its decoy: the tool says how the files' payments relate and leaves the diagnosis to the agent.
    [Fact]
    public async Task Two_files_for_one_batch_are_related_by_their_payments_without_naming_a_cause()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await Run(tools, "M-3310", "2026-11-02", "2026-11-06"));
        var json = ToolJson.Serialize(ok);

        Assert.Equal(["file:SF-FAB-1105", "file:SF-FAB-1105-R"], Fact(ok, "fact:payment-sets:fabrikam-pay:2026-11-05").SourceIds);
        Assert.Contains("\"relation\":\"identical\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("split", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_split_batch_is_disjoint()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await Run(tools, "M-3310", "2026-11-02", "2026-11-02"));

        Assert.Contains("\"relation\":\"disjoint\"", ToolJson.Serialize(ok), StringComparison.Ordinal);
    }

    // An empty settlement source looks like "no data", never like a missing batch.
    [Fact]
    public async Task An_empty_settlement_source_reports_no_batches_and_no_missing_file()
    {
        using var tools = new ToolFixture(new() { ["DemoData:Faults:Settlement:Mode"] = "Empty" });
        var ok = Ok(await Run(tools, "M-6230", "2026-11-05", "2026-11-10"));

        Assert.DoesNotContain(ok.Evidence, e => e.Id.StartsWith("fact:missing-file:", StringComparison.Ordinal));
        Assert.All(Json(ok)["processors"]!.AsArray(), p => Assert.Empty(p!["batches"]!["items"]!.AsArray()));
    }

    [Fact]
    public async Task A_transient_outage_is_retried_once_then_reported()
    {
        using var tools = new ToolFixture(new() { ["DemoData:Faults:Settlement:Mode"] = "Unavailable" });

        var failed = Failed(await Run(tools, "M-1042", "2026-11-09", "2026-11-10"), ToolErrorCode.Unavailable);
        Assert.Equal(DataSources.Settlement, failed.Source);
    }

    [Theory]
    [InlineData("M-404")]
    [InlineData("m-1042")]
    public async Task An_unknown_merchant_is_not_found(string merchant)
    {
        using var tools = new ToolFixture();

        Failed(await Run(tools, merchant, "2026-11-09", "2026-11-10"), ToolErrorCode.NotFound);
    }

    [Fact]
    public async Task A_range_past_today_is_refused_before_any_data_is_read()
    {
        using var tools = new ToolFixture(new() { ["DemoData:Faults:Merchants:Mode"] = "Unavailable" });

        var failed = Failed(await Run(tools, "M-1042", "2026-11-10", "2026-11-12"), ToolErrorCode.InvalidArgument);
        Assert.Equal("end_date can't be later than 2026-11-11.", failed.Message);
    }

    [Fact]
    public async Task A_user_without_the_reader_role_is_refused_before_any_data_is_read()
    {
        using var tools = new ToolFixture(new() { ["DemoData:Faults:Merchants:Mode"] = "Unavailable" });

        Failed(await tools.Tool<SettlementBatchesTool>().RunAsync(new SettlementBatchesRequest("M-1042", "2026-11-09", "2026-11-10"), TestUser.General, CancellationToken.None), ToolErrorCode.NotAuthorized);
    }

    [Fact]
    public async Task A_long_list_is_cut_and_says_so()
    {
        using var tools = new ToolFixture(maxListItems: 1);
        var ok = Ok(await Run(tools, "M-1042", "2026-11-09", "2026-11-10"));

        Assert.NotEmpty(ok.Truncations);
        Assert.Contains("\"truncated\":true", ToolJson.Serialize(ok), StringComparison.Ordinal);
    }

    // Labels are shown to people, never to the model, so they carry the reason as written, without markers.
    [Fact]
    public async Task A_closure_days_labels_show_its_reason_without_markers()
    {
        using var tools = new ToolFixture();
        var ok = Ok(await Run(tools, "M-6230", "2026-11-05", "2026-11-10"));

        Assert.All(ok.Evidence.Where(e => e.Id is "calendar:2026-11-06" or "fact:non-settlement-day:2026-11-06"), e => Assert.DoesNotContain(UntrustedText.Open, e.Label, StringComparison.Ordinal));
        Assert.Contains(UntrustedText.Open, ToolJson.Serialize(ok), StringComparison.Ordinal);
    }
}
