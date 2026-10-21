using PaymentOps.Domain.Investigation;

namespace PaymentOps.Data.Demo;

/// <summary>The dataset's as-of time, so the scenarios reason about the same days whenever they run.</summary>
internal sealed class DemoInvestigationClock(DateTimeOffset asOf) : IInvestigationClock
{
    public DateTimeOffset Now { get; } = asOf;

    public DateOnly Today { get; } = DateOnly.FromDateTime(asOf.UtcDateTime);
}
