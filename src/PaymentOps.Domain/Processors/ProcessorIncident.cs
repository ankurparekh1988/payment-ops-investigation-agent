namespace PaymentOps.Domain.Processors;

/// <summary>An incident a processor reported. <paramref name="ResolvedAt"/> is null while it's open.</summary>
public sealed record ProcessorIncident(
    string Id,
    string ProcessorId,
    ProcessorComponent Component,
    string Summary,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    string? ProcessorReference);

public enum ProcessorComponent
{
    Authorization,
    Settlement,
    FileDelivery,
}
