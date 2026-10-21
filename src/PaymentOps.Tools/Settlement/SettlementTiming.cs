using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Tools.Settlement;

/// <summary>How two or more files for one batch date relate by the payments they list. A fact, not a diagnosis.</summary>
public enum PaymentSetRelation
{
    Identical,
    Disjoint,
    Overlapping,
}

/// <param name="DecidingCaptureTime">The processor's capture time where recorded, else ours.</param>
/// <param name="SkippedDays">Non-settlement days passed over on the way to the expected batch.</param>
internal sealed record PaymentTiming(DateTimeOffset DecidingCaptureTime, DateTime LocalCaptureTime, bool AfterCutOff, DateOnly ExpectedBatchDate, IReadOnlyList<DateOnly> SkippedDays);

/// <summary>
/// The date and set arithmetic the runbooks ask for, done in code so the model never converts time zones or
/// counts settlement days. Each rule's tests quote the runbook sentence it implements.
/// </summary>
internal static class SettlementTiming
{
    // The processor decides the batch from its own capture time, judged in its own zone against its cut-off.
    public static PaymentTiming Timing(Payment payment, ProcessorConfiguration configuration, Func<DateOnly, bool> isSettlementDay)
    {
        var deciding = payment.ProcessorCapturedAt ?? payment.CapturedAt;
        var local = TimeZoneInfo.ConvertTime(deciding, Zone(configuration)).DateTime;
        var afterCutOff = TimeOnly.FromDateTime(local) >= configuration.CutOff;

        var day = DateOnly.FromDateTime(local).AddDays(afterCutOff ? 1 : 0);
        var skipped = new List<DateOnly>();
        while (!isSettlementDay(day))
        {
            skipped.Add(day);
            day = day.AddDays(1);
        }

        return new PaymentTiming(deciding, local, afterCutOff, day, skipped);
    }

    // A batch's file arrives the next morning at the processor's delivery time; it's missing once the grace has passed too.
    public static bool FileDue(DateOnly batchDate, ProcessorConfiguration configuration, DateTimeOffset now)
    {
        var expectedLocal = batchDate.AddDays(1).ToDateTime(configuration.ExpectedDelivery);
        var expected = new DateTimeOffset(expectedLocal, Zone(configuration).GetUtcOffset(expectedLocal));
        return now >= expected + configuration.LateAfter;
    }

    public static PaymentSetRelation Relate(IReadOnlyList<IReadOnlyCollection<string>> paymentSets)
    {
        var sets = paymentSets.Select(ids => ids.ToHashSet(StringComparer.Ordinal)).ToList();
        if (sets.Skip(1).All(set => set.SetEquals(sets[0])))
        {
            return PaymentSetRelation.Identical;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return sets.All(set => set.All(seen.Add)) ? PaymentSetRelation.Disjoint : PaymentSetRelation.Overlapping;
    }

    // The loader already refuses a time zone that isn't an IANA id this host can resolve.
    private static TimeZoneInfo Zone(ProcessorConfiguration configuration) => TimeZoneInfo.FindSystemTimeZoneById(configuration.TimeZoneId);
}
