namespace PaymentOps.Data.Demo.Faults;

/// <summary>One port's fault, from <c>DemoData:Faults:&lt;Port&gt;</c>.</summary>
internal sealed class FaultOptions
{
    public FaultMode Mode { get; init; }

    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(2);
}
