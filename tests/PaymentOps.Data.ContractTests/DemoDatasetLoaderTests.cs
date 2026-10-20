using System.Text.Json.Nodes;

using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.ContractTests;

public sealed class DemoDatasetLoaderTests
{
    [Fact]
    public void A_valid_dataset_loads()
    {
        var dataset = DatasetJson.Load(DatasetJson.Valid());

        Assert.Equal(new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero), dataset.AsOf);
        Assert.Equal("RF-37", Assert.Single(dataset.Merchants).RiskHold!.RuleId);
        Assert.Equal(["SF-1"], Assert.Single(dataset.Payouts).SourceFileIds);
        Assert.Equal(PayoutStatus.Paid, dataset.Payouts[0].Status);
    }

    [Fact]
    public void Unknown_properties_are_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("payments")["note"] = "anything";

        Assert.Throws<InvalidDemoDatasetException>(() => DatasetJson.Load(json));
    }

    [Theory]
    [InlineData("payments", "currency")]
    [InlineData("files", "lines")]
    [InlineData("payouts", "scheduledFor")]
    public void Missing_required_fields_are_rejected(string section, string field)
    {
        var json = DatasetJson.Valid();
        json.First(section).Remove(field);

        Assert.Throws<InvalidDemoDatasetException>(() => DatasetJson.Load(json));
    }

    [Fact]
    public void Optional_fields_are_written_out_as_null_rather_than_left_out()
    {
        var json = DatasetJson.Valid();
        json.First("incidents").Remove("resolvedAt");

        Assert.Throws<InvalidDemoDatasetException>(() => DatasetJson.Load(json));
    }

    [Theory]
    [InlineData("files", "lines")]
    [InlineData("payouts", "sourceFileIds")]
    [InlineData("merchants", "processorIds")]
    public void A_null_inside_a_list_is_rejected_not_a_crash(string section, string list)
    {
        var json = DatasetJson.Valid();
        json.First(section)[list]!.AsArray().Add(null);

        Assert.Throws<InvalidDemoDatasetException>(() => DatasetJson.Load(json));
    }

    [Fact]
    public void A_null_inside_a_nested_list_is_rejected_not_a_crash()
    {
        var json = DatasetJson.Valid();
        json.First("files")["lines"]![0]!["paymentIds"]!.AsArray().Add(null);

        Assert.Throws<InvalidDemoDatasetException>(() => DatasetJson.Load(json));
    }

    [Fact]
    public void A_card_like_number_anywhere_in_the_dataset_is_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("incidents")["summary"] = "Card 4242 4242 4242 4242 declined";

        DatasetJson.AssertRejected(json, "card-number checksum");
    }

    [Fact]
    public void A_missing_section_is_rejected()
    {
        var json = DatasetJson.Valid();
        json.Remove("errorEvents");

        Assert.Throws<InvalidDemoDatasetException>(() => DatasetJson.Load(json));
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var json = DatasetJson.Valid();
        json["incidents"]!.AsArray().Add(json.First("incidents").DeepClone());

        DatasetJson.AssertRejected(json, "INC-1", "more than once");
    }

    [Fact]
    public void A_merchant_routed_to_an_unknown_processor_is_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("merchants")["processorIds"]!.AsArray().Add("p9");

        DatasetJson.AssertRejected(json, "M-1", "p9");
    }

    [Fact]
    public void A_payment_for_a_processor_the_merchant_isnt_routed_to_is_rejected()
    {
        var json = DatasetJson.Valid();
        json["processors"]!.AsArray().Add(ProcessorTwo());
        json.First("payments")["processorId"] = "p2";

        DatasetJson.AssertRejected(json, "PAY-1", "p2");
    }

    [Fact]
    public void A_file_line_listing_an_unknown_payment_is_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("files")["lines"]![0]!["paymentIds"]!.AsArray().Add("PAY-404");

        DatasetJson.AssertRejected(json, "SF-1", "PAY-404");
    }

    [Fact]
    public void A_payment_batch_date_must_match_the_file_that_lists_it()
    {
        var json = DatasetJson.Valid();
        json.First("payments")["batchDate"] = "2026-10-30";

        DatasetJson.AssertRejected(json, "PAY-1", "SF-1");
    }

    [Fact]
    public void A_payment_with_a_batch_date_must_be_in_a_file()
    {
        var json = DatasetJson.Valid();
        json.First("files")["lines"]![0]!["paymentIds"] = new JsonArray();

        DatasetJson.AssertRejected(json, "PAY-1", "no file");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rejected")]
    [InlineData("other batch")]
    [InlineData("no line for the merchant")]
    public void A_payout_source_file_must_exist_be_accepted_and_match_processor_batch_and_merchant(string mismatch)
    {
        var json = DatasetJson.Valid();
        switch (mismatch)
        {
            case "missing": json.First("payouts")["sourceFileIds"] = new JsonArray("SF-404"); break;
            case "rejected": json.First("files")["status"] = "Rejected"; break;
            case "other batch": json.First("payouts")["batchDate"] = "2026-10-30"; break;
            case "no line for the merchant":
                json["merchants"]!.AsArray().Add(new JsonObject { ["id"] = "M-2", ["name"] = "Merchant Two", ["processorIds"] = new JsonArray("p1"), ["riskHold"] = null });
                json.First("payouts")["merchantId"] = "M-2";
                break;
        }

        DatasetJson.AssertRejected(json, "PO-1");
    }

    [Fact]
    public void A_payout_needs_at_least_one_source_file()
    {
        var json = DatasetJson.Valid();
        json.First("payouts")["sourceFileIds"] = new JsonArray();

        DatasetJson.AssertRejected(json, "PO-1");
    }

    [Theory]
    [InlineData("Paid", null)]
    [InlineData("Scheduled", "2026-11-03T12:00:00Z")]
    public void Only_a_paid_payout_has_a_paid_time(string status, string? paidAt)
    {
        var json = DatasetJson.Valid();
        json.First("payouts")["status"] = status;
        json.First("payouts")["paidAt"] = paidAt;

        DatasetJson.AssertRejected(json, "PO-1");
    }

    [Fact]
    public void Unknown_time_zones_are_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("processors")["configuration"]!["timeZoneId"] = "Mars/Olympus";

        DatasetJson.AssertRejected(json, "p1", "Mars/Olympus");
    }

    [Theory]
    [InlineData("payments", "usd")]
    [InlineData("payouts", "US")]
    public void Currencies_must_be_three_upper_case_letters(string section, string currency)
    {
        var json = DatasetJson.Valid();
        json.First(section)["currency"] = currency;

        DatasetJson.AssertRejected(json, currency);
    }

    [Theory]
    [InlineData("payments")]
    [InlineData("payouts")]
    public void Amounts_must_be_positive(string section)
    {
        var json = DatasetJson.Valid();
        json.First(section)["amount"] = 0;

        DatasetJson.AssertRejected(json, "amount");
    }

    [Theory]
    [InlineData("2026-11-07")] // a Saturday
    [InlineData("2026-11-11")] // already listed
    public void Closure_days_must_be_unique_weekdays(string date)
    {
        var json = DatasetJson.Valid();
        json["closureDays"]!.AsArray().Add(new JsonObject { ["date"] = date, ["reason"] = "Another" });

        DatasetJson.AssertRejected(json, date);
    }

    [Theory]
    [InlineData("asOf", null)]
    [InlineData("payments", "capturedAt")]
    [InlineData("files", "receivedAt")]
    [InlineData("incidents", "startedAt")]
    [InlineData("errorEvents", "occurredAt")]
    public void Timestamps_must_be_utc(string section, string? field)
    {
        var json = DatasetJson.Valid();
        if (field is null)
        {
            json[section] = "2026-11-03T13:00:00-05:00";
        }
        else
        {
            json.First(section)[field] = "2026-11-02T14:05:00-05:00";
        }

        DatasetJson.AssertRejected(json, "UTC");
    }

    [Theory]
    [InlineData("payments", "capturedAt")]
    [InlineData("files", "receivedAt")]
    [InlineData("incidents", "startedAt")]
    [InlineData("errorEvents", "occurredAt")]
    [InlineData("payouts", "paidAt")]
    public void Nothing_happens_after_as_of(string section, string field)
    {
        var json = DatasetJson.Valid();
        json.First(section)[field] = "2026-11-04T09:00:00Z";

        DatasetJson.AssertRejected(json, "after", "as-of");
    }

    [Fact]
    public void A_scheduled_payout_may_be_due_after_as_of()
    {
        var json = DueAfterAsOf("Scheduled");

        Assert.Equal(new DateOnly(2026, 11, 5), DatasetJson.Load(json).Payouts[0].ScheduledFor);
    }

    [Fact]
    public void A_held_payout_may_not_be_due_after_as_of() =>
        DatasetJson.AssertRejected(DueAfterAsOf("Held"), "PO-1");

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var json = DatasetJson.Valid();
        json.First("payments")["currency"] = "usd";
        json.First("processors")["configuration"]!["timeZoneId"] = "Mars/Olympus";

        Assert.Equal(2, DatasetJson.Errors(json).Count);
    }

    private static JsonObject DueAfterAsOf(string status)
    {
        var json = DatasetJson.Valid();
        json.First("payouts")["status"] = status;
        json.First("payouts")["paidAt"] = null;
        json.First("payouts")["scheduledFor"] = "2026-11-05";
        return json;
    }

    private static JsonObject ProcessorTwo() => new()
    {
        ["id"] = "p2",
        ["name"] = "Processor Two",
        ["configuration"] = new JsonObject
        {
            ["cutOff"] = "17:00:00",
            ["timeZoneId"] = "Europe/London",
            ["expectedDelivery"] = "03:00:00",
            ["lateAfter"] = "02:00:00",
            ["holdingSettlementDays"] = 1,
        },
    };
}
