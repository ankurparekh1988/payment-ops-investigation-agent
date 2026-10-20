namespace PaymentOps.Data.Demo.Faults;

/// <summary>One port's fault, from <c>DemoData:Faults:&lt;Port&gt;</c>.</summary>
internal sealed record FaultOptions(FaultMode Mode, TimeSpan Delay);
