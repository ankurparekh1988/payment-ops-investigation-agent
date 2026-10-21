namespace PaymentOps.Data.Demo;

internal static class SettlementCalendar
{
    /// <summary>Weekends are never settlement days, so the dataset lists only weekday closures.</summary>
    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
