using Microsoft.Extensions.Options;

using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;

namespace PaymentOps.Tools.Processors;

public sealed record ProcessorStatusRequest(string? ProcessorId, string? StartDate, string? EndDate);

public sealed record ProcessorStatus(string Id, string Name, string EvidenceId, ProcessorSettings Configuration, ListOf<IncidentView> Incidents);

/// <param name="CutOff">Local time in <paramref name="TimeZone"/>, as is <paramref name="ExpectedDelivery"/>.</param>
public sealed record ProcessorSettings(TimeOnly CutOff, string TimeZone, TimeOnly ExpectedDelivery, TimeSpan LateAfter, int HoldingSettlementDays);

/// <param name="Summary">The processor's own words, marked as untrusted data.</param>
public sealed record IncidentView(string Id, string Component, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt, string? Reference, string Summary, string EvidenceId);

/// <summary>A processor's configured settlement settings and the incidents it reported that overlap the range.</summary>
public sealed class ProcessorStatusTool(IProcessorData processors, IInvestigationClock clock, IOptions<ToolOptions> options)
    : InvestigationTool<ProcessorStatusRequest>(options)
{
    public override string Name => ToolNames.ProcessorStatus;

    public override string Description =>
        "A processor's configuration (cut-off and time zone, expected file delivery, holding days) and the incidents it reported " +
        "that overlap start_date to end_date, both included, with the component each affected.";

    public override string Source => DataSources.Processors;

    protected override async Task<ToolResult> RunCoreAsync(ProcessorStatusRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        var processorId = ToolArguments.Id(request.ProcessorId, "processor_id");
        var range = ToolArguments.Range(request.StartDate, request.EndDate, clock.Today);
        if (await ReadAsync(token => processors.FindAsync(processorId, token), DataSources.Processors, cancellationToken) is not { } processor)
        {
            return NotFound("processor", processorId);
        }

        var incidents = await ReadAsync(token => processors.GetIncidentsAsync(processorId, range.FromUtc, range.ToUtcExclusive, token), DataSources.Processors, cancellationToken);
        var evidence = Collect();
        var views = incidents.Select(i => new IncidentView(
            i.Id, ToolJson.Word(i.Component), i.StartedAt, i.ResolvedAt, i.ProcessorReference, UntrustedText.Wrap(i.Summary),
            evidence.Record(EvidenceIds.Incident(i.Id), $"Incident {i.Id}"))).ToList();

        var c = processor.Configuration;
        return evidence.Ok(new ProcessorStatus(
            processor.Id,
            processor.Name,
            evidence.Record(EvidenceIds.Processor(processor.Id), processor.Name),
            new ProcessorSettings(c.CutOff, c.TimeZoneId, c.ExpectedDelivery, c.LateAfter, c.HoldingSettlementDays),
            evidence.Cap(views, "Incidents")));
    }
}
