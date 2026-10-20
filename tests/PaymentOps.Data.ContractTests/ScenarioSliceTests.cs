using PaymentOps.Data.Demo;
using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.ContractTests;

/// <summary>
/// Each investigation scenario can be worked out from the ports alone, the way the runbooks say to:
/// these tests ask only what an investigator could ask, and check the answer the data gives.
/// </summary>
public sealed class ScenarioSliceTests
{
    private const string Fabrikam = "fabrikam-pay";
    private const string Northwind = "northwind-acquiring";
    private static readonly CancellationToken None = CancellationToken.None;

    // Loaded once: the records are immutable, and xUnit makes a new instance for every test.
    private static readonly DemoDataset Dataset = DemoDatasetLoader.LoadDefault();

    private readonly DemoDataset _dataset = Dataset;
    private readonly DemoMerchantData _merchants = new(Dataset);
    private readonly DemoSettlementData _settlement = new(Dataset);
    private readonly DemoProcessorData _processors = new(Dataset);
    private readonly DemoErrorEventData _events = new(Dataset);

    [Fact]
    public void The_shipped_dataset_loads_as_of_a_tuesday_evening()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 22, 0, 0, TimeSpan.Zero), _dataset.AsOf);
        Assert.Equal(DayOfWeek.Tuesday, _dataset.AsOf.DayOfWeek);
    }

    // S1: M-1042's Fabrikam payout for Monday is missing because Fabrikam's batch never closed.
    [Fact]
    public async Task S1_a_processor_outage_over_the_cut_off_left_the_batch_unclosed()
    {
        var monday = new DateOnly(2026, 11, 9);
        var merchant = await _merchants.FindAsync("M-1042", None);
        Assert.Equal([Fabrikam, Northwind], merchant!.ProcessorIds);

        var fabrikam = (await _processors.FindAsync(Fabrikam, None))!;
        var cutOff = CutOffUtc(fabrikam, monday);

        var payments = await _settlement.GetPaymentsAsync("M-1042", Day(monday), Day(monday.AddDays(1)), None);
        var fabrikamPayments = payments.Where(p => p.ProcessorId == Fabrikam).ToList();
        Assert.Equal(3, fabrikamPayments.Count);
        Assert.All(fabrikamPayments, p => Assert.True(p.CapturedAt < cutOff));
        Assert.All(fabrikamPayments, p => Assert.Null(p.BatchDate));

        var incident = Assert.Single(await _processors.GetIncidentsAsync(Fabrikam, cutOff, cutOff.AddMinutes(1), None));
        Assert.Equal(ProcessorComponent.Settlement, incident.Component);
        Assert.True(incident.StartedAt < cutOff);
        Assert.Null(incident.ResolvedAt);

        var window = (From: Day(monday).AddHours(12), To: _dataset.AsOf);
        var fabrikamErrors = await _events.GetEventsAsync(new(Fabrikam, null, window.From, window.To), None);
        Assert.True(fabrikamErrors.Count(e => e.Category == ErrorCategory.FileTransferTimeout) >= 3);
        Assert.Empty(await _events.GetEventsAsync(new(Northwind, null, window.From, window.To), None));

        Assert.Empty(await _settlement.GetFilesAsync(Fabrikam, monday, monday.AddDays(1), None));

        // The other processor's batch for the same day arrived, and its payout is on Northwind's normal schedule.
        var northwindFile = Assert.Single(await _settlement.GetFilesAsync(Northwind, monday, monday.AddDays(1), None));
        Assert.Contains(northwindFile.Lines, l => l.MerchantId == "M-1042" && l.PaymentIds.Contains("PAY-1042-05"));
        var northwindPayout = Assert.Single(await _settlement.GetPayoutsAsync("M-1042", monday, monday.AddDays(1), None));
        Assert.Equal(PayoutStatus.Scheduled, northwindPayout.Status);
        Assert.Equal(await SettlementDaysAfter(monday, 2), northwindPayout.ScheduledFor);
    }

    [Fact]
    public async Task S1_distractor_an_earlier_authorization_only_incident()
    {
        var incidents = await _processors.GetIncidentsAsync(Fabrikam, Day(new(2026, 11, 2)), _dataset.AsOf, None);

        Assert.Contains(incidents, i => i.Component == ProcessorComponent.Authorization && i.ResolvedAt is not null);
        var declines = await _events.GetEventsAsync(new(Fabrikam, "M-1042", Day(new(2026, 11, 4)), Day(new(2026, 11, 5))), None);
        Assert.All(declines, e => Assert.Equal(ErrorCategory.AuthorizationDeclined, e.Category));
    }

    // S2: a capture just after Northwind's cut-off, before a closure day and a weekend, is paid on time.
    [Fact]
    public async Task S2_a_missed_cut_off_before_a_closure_day_moves_the_payout_but_it_is_on_time()
    {
        var thursday = new DateOnly(2026, 11, 5);
        var northwind = (await _processors.FindAsync(Northwind, None))!;

        var payment = (await _settlement.GetPaymentsAsync("M-6230", Day(thursday), Day(thursday.AddDays(1)), None))
            .Single(p => p.Id == "PAY-6230-04");
        var cutOff = CutOffUtc(northwind, thursday);
        Assert.InRange(payment.CapturedAt - cutOff, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

        var calendar = await _settlement.GetCalendarAsync(thursday, thursday.AddDays(5), None);
        Assert.Equal([true, false, false, false, true], calendar.Select(d => d.IsSettlementDay));
        Assert.NotNull(calendar[1].ClosureReason);

        var nextSettlementDay = await SettlementDaysAfter(thursday, 1);
        Assert.Equal(nextSettlementDay, payment.BatchDate);

        var payout = Assert.Single(await _settlement.GetPayoutsAsync("M-6230", nextSettlementDay, nextSettlementDay.AddDays(1), None));
        Assert.Equal(await SettlementDaysAfter(nextSettlementDay, northwind.Configuration.HoldingSettlementDays), payout.ScheduledFor);
        Assert.Equal(PayoutStatus.Scheduled, payout.Status);
        Assert.True(payout.ScheduledFor > DateOnly.FromDateTime(_dataset.AsOf.UtcDateTime));
    }

    [Fact]
    public async Task S2_distractors_captures_before_the_cut_off_were_paid_on_time()
    {
        var thursday = new DateOnly(2026, 11, 5);

        var payout = Assert.Single(await _settlement.GetPayoutsAsync("M-6230", thursday, thursday.AddDays(1), None));
        var file = Assert.Single(await _settlement.GetFilesAsync(Northwind, thursday, thursday.AddDays(1), None));

        Assert.Equal(PayoutStatus.Paid, payout.Status);
        Assert.Equal(await SettlementDaysAfter(thursday, 2), payout.ScheduledFor);
        Assert.Equal(["PAY-6230-02", "PAY-6230-03"], file.Lines.Single(l => l.MerchantId == "M-6230").PaymentIds);
    }

    // S3: payouts are paused by an RF-37 hold, while payments and files carry on as normal.
    [Fact]
    public async Task S3_a_risk_hold_pauses_payouts_only()
    {
        var merchant = (await _merchants.FindAsync("M-2087", None))!;
        Assert.Equal("RF-37", merchant.RiskHold!.RuleId);
        Assert.True(merchant.RiskHold.ReviewBy > DateOnly.FromDateTime(_dataset.AsOf.UtcDateTime));

        var payouts = await _settlement.GetPayoutsAsync("M-2087", new(2026, 11, 1), new(2026, 11, 11), None);
        Assert.NotEmpty(payouts);
        Assert.All(payouts, p => Assert.Equal(PayoutStatus.Held, p.Status));

        var files = await _settlement.GetFilesAsync(Northwind, new(2026, 11, 1), new(2026, 11, 11), None);
        Assert.Equal(payouts.Count, files.Count(f => f.Status == SettlementFileStatus.Accepted && f.Lines.Any(l => l.MerchantId == "M-2087")));
    }

    [Fact]
    public async Task S3_distractor_a_late_payout_after_a_rejected_file()
    {
        var wednesday = new DateOnly(2026, 11, 4);
        var files = await _settlement.GetFilesAsync(Fabrikam, wednesday, wednesday.AddDays(1), None);

        Assert.Equal([SettlementFileStatus.Rejected, SettlementFileStatus.Accepted], files.OrderBy(f => f.ReceivedAt).Select(f => f.Status));
        var payout = Assert.Single(await _settlement.GetPayoutsAsync("M-3310", wednesday, wednesday.AddDays(1), None));
        Assert.Equal(files.Single(f => f.Status == SettlementFileStatus.Accepted).Id, Assert.Single(payout.SourceFileIds));
        Assert.True(payout.ScheduledFor > await SettlementDaysAfter(wednesday, 1));
        Assert.Contains(await _events.GetEventsAsync(new(Fabrikam, null, Day(wednesday.AddDays(1)), Day(wednesday.AddDays(2))), None),
            e => e.Category == ErrorCategory.FileRejected);
    }

    // S4: Northwind never splits, so a second file for the same batch with the same payments is a resend.
    [Fact]
    public async Task S4_a_resent_file_after_a_failed_acknowledgement_created_a_second_payout()
    {
        var tuesday = new DateOnly(2026, 11, 3);
        var files = (await _settlement.GetFilesAsync(Northwind, tuesday, tuesday.AddDays(1), None)).OrderBy(f => f.ReceivedAt).ToList();

        Assert.Equal(2, files.Count);
        Assert.Equal(PaymentIds(files[0]), PaymentIds(files[1]));

        var ack = Assert.Single(await _events.GetEventsAsync(new(Northwind, null, files[0].ReceivedAt, files[1].ReceivedAt), None));
        Assert.Equal(ErrorCategory.AcknowledgementFailed, ack.Category);
        Assert.Equal(files[0].Id, ack.FileId);

        foreach (var merchantId in files[0].Lines.Select(l => l.MerchantId))
        {
            var payouts = await _settlement.GetPayoutsAsync(merchantId, tuesday, tuesday.AddDays(1), None);
            var original = Assert.Single(payouts, p => p.SourceFileIds.SequenceEqual([files[0].Id]));
            var duplicate = Assert.Single(payouts, p => p.SourceFileIds.SequenceEqual([files[1].Id]));
            Assert.Equal(PayoutStatus.Paid, original.Status);
            Assert.Equal(PayoutStatus.Scheduled, duplicate.Status);
            Assert.Equal(original.Amount, duplicate.Amount);
        }
    }

    [Fact]
    public async Task S4_distractor_a_fabrikam_split_batch_is_two_files_with_disjoint_payments()
    {
        var monday = new DateOnly(2026, 11, 2);
        var files = await _settlement.GetFilesAsync(Fabrikam, monday, monday.AddDays(1), None);

        Assert.Equal(2, files.Count);
        Assert.Empty(PaymentIds(files[0]).Intersect(PaymentIds(files[1])));

        var payout = Assert.Single(await _settlement.GetPayoutsAsync("M-3310", monday, monday.AddDays(1), None));
        Assert.Equal(files.Select(f => f.Id).Order(StringComparer.Ordinal), payout.SourceFileIds.Order(StringComparer.Ordinal));
        Assert.Equal(files.SelectMany(f => f.Lines).Where(l => l.MerchantId == "M-3310").Sum(l => l.Amount), payout.Amount);
    }

    private static IEnumerable<string> PaymentIds(SettlementFile file) =>
        file.Lines.SelectMany(l => l.PaymentIds).Order(StringComparer.Ordinal);

    private static DateTimeOffset Day(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    // The cut-off is a local time in the processor's zone; investigations compare it in UTC.
    private static DateTimeOffset CutOffUtc(Processor processor, DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(processor.Configuration.TimeZoneId);
        var local = date.ToDateTime(processor.Configuration.CutOff);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private async Task<DateOnly> SettlementDaysAfter(DateOnly date, int count)
    {
        var days = await _settlement.GetCalendarAsync(date.AddDays(1), date.AddDays(15), None);
        return days.Where(d => d.IsSettlementDay).Skip(count - 1).First().Date;
    }
}
