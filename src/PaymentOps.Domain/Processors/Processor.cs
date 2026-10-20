namespace PaymentOps.Domain.Processors;

/// <summary>A processor we route payments to, with its configured settlement settings.</summary>
public sealed record Processor(string Id, string Name, ProcessorConfiguration Configuration);

/// <summary>
/// <paramref name="CutOff"/> and <paramref name="ExpectedDelivery"/> are local times in <paramref name="TimeZoneId"/>
/// (IANA). <paramref name="LateAfter"/> is the grace after the expected delivery before a file counts as late.
/// <paramref name="HoldingSettlementDays"/> counts settlement days between batch close and release.
/// </summary>
public sealed record ProcessorConfiguration(
    TimeOnly CutOff,
    string TimeZoneId,
    TimeOnly ExpectedDelivery,
    TimeSpan LateAfter,
    int HoldingSettlementDays);
