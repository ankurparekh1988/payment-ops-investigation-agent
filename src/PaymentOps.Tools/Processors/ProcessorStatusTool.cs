using Microsoft.Extensions.Options;

using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;

namespace PaymentOps.Tools.Processors;

public sealed record ProcessorStatusRequest(string? ProcessorId, string? StartDate, string? EndDate);

public sealed record ProcessorStatus(string Id, string Name, string EvidenceId, ProcessorSettings Configuration, ListOf<IncidentView> Incidents);

/// <param name="CutOff">Local time in <paramref name="TimeZone"/>, as are <paramref name="ExpectedDelivery"/>.</param>
public sealed record ProcessorSettings(TimeOnly CutOff, string TimeZone, TimeOnly ExpectedDelivery, TimeSpan LateAfter, int HoldingSettlementDays);

/// <param name="Summary">The processor's own words, marked as untrusted data.</param>
public sealed record IncidentView(string Id, string Component, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt, string? Reference, string Summary, string EvidenceId);

/// <summary>A processor's configured settlement settings and the incidents it reported that overlap the range.</summary>
public sealed class ProcessorStatusTool(IProcessorData processors, IInvestigationClock clock, IOptions<ToolOptions> options) : InvestigationTool<ProcessorStatusRequest>
{
    public override string Name => ToolNames.ProcessorStatus;

    public override string Description =>
        "A processor's configuration (cut-off and time zone, expected file delivery, holding days) and the incidents it reported " +
        "that overlap start_date to end_date, both included, with the component each affected.";

    public override string Source => DataSources.Processors;

    protected override async Task<ToolResult> RunCoreAsync(ProcessorStatusRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryId(request.ProcessorId, "processor_id", out var processorId, out var error))
        {
            return Invalid(error!);
        }

        if (!ToolArguments.TryRange(request.StartDate, request.EndDate, clock.Today, out var range, out var code, out error))
        {
            return Invalid(error!, code);
        }

        var backoff = options.Value.RetryBackoff;
        var processor = await DataSourceCall.RunAsync(token => processors.FindAsync(processorId, token), DataSources.Processors, backoff, cancellationToken);
        if (processor is null)
        {
            return new ToolResult.Failed(ToolErrorCode.NotFound, $"No processor has the id {processorId}.");
        }

        var incidents = await DataSourceCall.RunAsync(token => processors.GetIncidentsAsync(processorId, range.FromUtc, range.ToUtcExclusive, token), DataSources.Processors, backoff, cancellationToken);
        var views = incidents.Select(i => new IncidentView(i.Id, ToolJson.Word(i.Component), i.StartedAt, i.ResolvedAt, i.ProcessorReference, UntrustedText.Wrap(i.Summary), EvidenceIds.Incident(i.Id))).ToList();
        var shown = ListOf.Capped(views, options.Value.MaxListItems, "Incidents", out var truncation);

        var evidence = new List<EvidenceItem> { new RecordEvidence(EvidenceIds.Processor(processor.Id), processor.Name) };
        evidence.AddRange(incidents.Select(i => new RecordEvidence(EvidenceIds.Incident(i.Id), $"Incident {i.Id}")));

        var configuration = processor.Configuration;
        var settings = new ProcessorSettings(configuration.CutOff, configuration.TimeZoneId, configuration.ExpectedDelivery, configuration.LateAfter, configuration.HoldingSettlementDays);
        return new ToolResult.Ok(new ProcessorStatus(processor.Id, processor.Name, EvidenceIds.Processor(processor.Id), settings, shown), evidence, truncation is null ? [] : [truncation]);
    }
}
