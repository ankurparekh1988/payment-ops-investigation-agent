using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;
using PaymentOps.Tools.Settlement;

namespace PaymentOps.Tools.Tests;

public sealed class SettlementTimingTests
{
    private static readonly ProcessorConfiguration London = new(new TimeOnly(17, 0), "Europe/London", new TimeOnly(3, 0), TimeSpan.FromHours(3), 2);
    private static readonly ProcessorConfiguration NewYork = new(new TimeOnly(16, 0), "America/New_York", new TimeOnly(2, 0), TimeSpan.FromHours(2), 0);
    private static readonly DateOnly Closure = new(2026, 11, 6);

    // Weekends and the one closure day aren't settlement days.
    private static bool IsSettlementDay(DateOnly date) =>
        date != Closure && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private static Payment Captured(DateTimeOffset ours, DateTimeOffset? processors = null) =>
        new("PAY-1", "M-1", "p", 10m, "GBP", ours, processors, null);

    private static DateTimeOffset Utc(int day, int hour, int minute, int month = 11) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    // "Payments captured that day join the next settlement day's batch" (settlement-closure-days-policy), and "a
    // payment captured after the cut-off belongs to the next settlement day's batch" (fabrikam-pay-operating-guide).
    [Fact]
    public void A_capture_after_the_cut_off_before_a_closure_day_and_weekend_lands_on_the_monday()
    {
        var timing = SettlementTiming.Timing(Captured(Utc(5, 17, 5), Utc(5, 17, 5)), London, IsSettlementDay);

        Assert.True(timing.AfterCutOff);
        Assert.Equal(new DateOnly(2026, 11, 9), timing.ExpectedBatchDate);
        Assert.Equal([new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8)], timing.SkippedDays);
    }

    [Fact]
    public void A_capture_before_the_cut_off_is_in_that_days_batch()
    {
        var timing = SettlementTiming.Timing(Captured(Utc(5, 16, 55)), London, IsSettlementDay);

        Assert.False(timing.AfterCutOff);
        Assert.Equal(new DateOnly(2026, 11, 5), timing.ExpectedBatchDate);
        Assert.Empty(timing.SkippedDays);
    }

    // "Use the processor's recorded capture time where we have it" (settlement-pending-investigation).
    [Fact]
    public void The_processors_capture_time_decides_when_it_differs_from_ours()
    {
        var timing = SettlementTiming.Timing(Captured(Utc(4, 16, 59), Utc(4, 17, 1)), London, IsSettlementDay);

        Assert.Equal(Utc(4, 17, 1), timing.DecidingCaptureTime);
        Assert.True(timing.AfterCutOff);
        Assert.Equal(new DateOnly(2026, 11, 5), timing.ExpectedBatchDate);
    }

    // "Convert the capture time into that time zone before comparing" (settlement-pending-investigation).
    [Fact]
    public void A_capture_late_in_the_utc_day_is_judged_on_the_processors_local_day()
    {
        var timing = SettlementTiming.Timing(Captured(Utc(5, 23, 30)), NewYork, IsSettlementDay);

        Assert.Equal(new DateTime(2026, 11, 5, 18, 30, 0), timing.LocalCaptureTime);
        Assert.True(timing.AfterCutOff);
    }

    // New York leaves daylight time on 1 November 2026, so the same UTC time is after the cut-off before it and before the cut-off after it.
    [Theory]
    [InlineData(10, 30, true)]
    [InlineData(11, 2, false)]
    public void The_cut_off_follows_the_zones_offset_on_the_day(int month, int day, bool afterCutOff) =>
        Assert.Equal(afterCutOff, SettlementTiming.Timing(Captured(Utc(day, 20, 30, month)), NewYork, IsSettlementDay).AfterCutOff);

    // London leaves summer time on 25 October 2026: the 17:00 cut-off is 16:00 UTC before it and 17:00 UTC after.
    [Theory]
    [InlineData(23, true)]
    [InlineData(26, false)]
    public void The_london_cut_off_follows_summer_time(int day, bool afterCutOff) =>
        Assert.Equal(afterCutOff, SettlementTiming.Timing(Captured(Utc(day, 16, 30, 10)), London, IsSettlementDay).AfterCutOff);

    // The batch's file is expected the next morning at the processor's delivery time, plus its grace period.
    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 1, true)]
    public void A_file_is_due_once_its_expected_delivery_and_grace_have_passed(int hour, int minute, bool due) =>
        Assert.Equal(due, SettlementTiming.FileDue(new DateOnly(2026, 11, 9), NewYork, Utc(10, hour, minute)));

    [Theory]
    [InlineData(new[] { "a", "b" }, new[] { "b", "a" }, PaymentSetRelation.Identical)]
    [InlineData(new[] { "a", "b" }, new[] { "c" }, PaymentSetRelation.Disjoint)]
    [InlineData(new[] { "a", "b" }, new[] { "b", "c" }, PaymentSetRelation.Overlapping)]
    public void Payment_sets_relate_without_naming_a_cause(string[] first, string[] second, PaymentSetRelation expected) =>
        Assert.Equal(expected, SettlementTiming.Relate([first, second]));

    [Fact]
    public void Ids_that_differ_only_in_case_are_different_payments() =>
        Assert.Equal(PaymentSetRelation.Disjoint, SettlementTiming.Relate([["PAY-1"], ["pay-1"]]));
}
