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

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("INC-1", StringComparison.Ordinal) && e.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_merchant_routed_to_an_unknown_processor_is_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("merchants")["processorIds"]!.AsArray().Add("p9");

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("M-1", StringComparison.Ordinal) && e.Contains("p9", StringComparison.Ordinal));
    }

    [Fact]
    public void A_payment_for_a_processor_the_merchant_isnt_routed_to_is_rejected()
    {
        var json = DatasetJson.Valid();
        json["processors"]!.AsArray().Add(ProcessorTwo());
        json.First("payments")["processorId"] = "p2";

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PAY-1", StringComparison.Ordinal) && e.Contains("p2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_line_listing_an_unknown_payment_is_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("files")["lines"]![0]!["paymentIds"]!.AsArray().Add("PAY-404");

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("SF-1", StringComparison.Ordinal) && e.Contains("PAY-404", StringComparison.Ordinal));
    }

    [Fact]
    public void A_payment_batch_date_must_match_the_file_that_lists_it()
    {
        var json = DatasetJson.Valid();
        json.First("payments")["batchDate"] = "2026-10-30";

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PAY-1", StringComparison.Ordinal) && e.Contains("SF-1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_payment_with_a_batch_date_must_be_in_a_file()
    {
        var json = DatasetJson.Valid();
        json.First("files")["lines"]![0]!["paymentIds"] = new JsonArray();

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PAY-1", StringComparison.Ordinal) && e.Contains("no file", StringComparison.Ordinal));
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

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PO-1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_payout_needs_at_least_one_source_file()
    {
        var json = DatasetJson.Valid();
        json.First("payouts")["sourceFileIds"] = new JsonArray();

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PO-1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Paid", null)]
    [InlineData("Scheduled", "2026-11-03T12:00:00Z")]
    public void Only_a_paid_payout_has_a_paid_time(string status, string? paidAt)
    {
        var json = DatasetJson.Valid();
        json.First("payouts")["status"] = status;
        json.First("payouts")["paidAt"] = paidAt;

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PO-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_time_zones_are_rejected()
    {
        var json = DatasetJson.Valid();
        json.First("processors")["configuration"]!["timeZoneId"] = "Mars/Olympus";

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("p1", StringComparison.Ordinal) && e.Contains("Mars/Olympus", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("payments", "usd")]
    [InlineData("payouts", "US")]
    public void Currencies_must_be_three_upper_case_letters(string section, string currency)
    {
        var json = DatasetJson.Valid();
        json.First(section)["currency"] = currency;

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains(currency, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("payments")]
    [InlineData("payouts")]
    public void Amounts_must_be_positive(string section)
    {
        var json = DatasetJson.Valid();
        json.First(section)["amount"] = 0;

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("amount", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026-11-07")] // a Saturday
    [InlineData("2026-11-11")] // already listed
    public void Closure_days_must_be_unique_weekdays(string date)
    {
        var json = DatasetJson.Valid();
        json["closureDays"]!.AsArray().Add(new JsonObject { ["date"] = date, ["reason"] = "Another" });

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains(date, StringComparison.Ordinal));
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

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("UTC", StringComparison.Ordinal));
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

        Assert.Contains(DatasetJson.Errors(json), e => e.Contains("after", StringComparison.Ordinal) && e.Contains("as-of", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Scheduled", false)]
    [InlineData("Held", true)]
    public void Only_scheduled_payouts_may_be_due_after_as_of(string status, bool rejected)
    {
        var json = DatasetJson.Valid();
        json.First("payouts")["status"] = status;
        json.First("payouts")["paidAt"] = null;
        json.First("payouts")["scheduledFor"] = "2026-11-05";

        if (rejected)
        {
            Assert.Contains(DatasetJson.Errors(json), e => e.Contains("PO-1", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(new DateOnly(2026, 11, 5), DatasetJson.Load(json).Payouts[0].ScheduledFor);
        }
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var json = DatasetJson.Valid();
        json.First("payments")["currency"] = "usd";
        json.First("processors")["configuration"]!["timeZoneId"] = "Mars/Olympus";

        Assert.Equal(2, DatasetJson.Errors(json).Count);
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
