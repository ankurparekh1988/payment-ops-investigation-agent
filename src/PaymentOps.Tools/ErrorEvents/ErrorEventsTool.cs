using System.Globalization;

using Microsoft.Extensions.Options;

using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;

namespace PaymentOps.Tools.ErrorEvents;

public sealed record ErrorEventsRequest(string? ProcessorId, string? MerchantId, string? StartDate, string? EndDate);

public sealed record ErrorEventSummary(ListOf<ErrorEventGroup> Groups);

/// <param name="EvidenceId">Supports the count: its sources are every event in the group, not only those shown.</param>
public sealed record ErrorEventGroup(string ProcessorId, string Category, string Code, int Count, DateTimeOffset First, DateTimeOffset Last, ListOf<string> EventIds, string EvidenceId);

/// <summary>Our own error events for a processor, a merchant or both, grouped so repeats read as one pattern.</summary>
public sealed class ErrorEventsTool(IErrorEventData events, IInvestigationClock clock, IOptions<ToolOptions> options) : InvestigationTool<ErrorEventsRequest>
{
    public override string Name => ToolNames.ErrorEvents;

    public override string Description =>
        "Errors our platform recorded with processors from start_date to end_date, both included, for a processor_id, a merchant_id or both, " +
        "grouped by category and code with a count and first and last times. Events not tied to a merchant appear only when filtering by processor.";

    public override string Source => DataSources.ErrorEvents;

    protected override async Task<ToolResult> RunCoreAsync(ErrorEventsRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        if (request.ProcessorId is null && request.MerchantId is null)
        {
            return Invalid("Give a processor_id, a merchant_id, or both.");
        }

        string? processorId = null;
        string? merchantId = null;
        string? error = null;
        if ((request.ProcessorId is not null && !ToolArguments.TryId(request.ProcessorId, "processor_id", out processorId, out error))
            || (request.MerchantId is not null && !ToolArguments.TryId(request.MerchantId, "merchant_id", out merchantId, out error)))
        {
            return Invalid(error!);
        }

        if (!ToolArguments.TryRange(request.StartDate, request.EndDate, clock.Today, out var range, out var code, out error))
        {
            return Invalid(error!, code);
        }

        var found = await DataSourceCall.RunAsync(
            token => events.GetEventsAsync(new ErrorEventQuery(processorId, merchantId, range.FromUtc, range.ToUtcExclusive), token),
            DataSources.ErrorEvents,
            options.Value.RetryBackoff,
            cancellationToken);

        var cap = options.Value.MaxListItems;
        var truncations = new List<Truncation>();
        var evidence = new List<EvidenceItem>(found.Select(e => new RecordEvidence(EvidenceIds.Event(e.Id), $"Error event {e.Id}")));
        var groups = new List<ErrorEventGroup>();
        foreach (var group in found.GroupBy(e => (e.ProcessorId, e.Category, e.Code)))
        {
            var members = group.ToList();
            var category = ToolJson.Word(group.Key.Category);
            var factId = EvidenceIds.EventGroup(group.Key.ProcessorId, merchantId, category, group.Key.Code, range);
            evidence.Add(new DerivedEvidence(factId, string.Create(CultureInfo.InvariantCulture, $"{members.Count} {category} events from {group.Key.ProcessorId}"), [.. members.Select(e => EvidenceIds.Event(e.Id))]));

            var ids = ListOf.Capped([.. members.Select(e => e.Id)], cap, $"{category} event ids", out var truncation);
            if (truncation is not null)
            {
                truncations.Add(truncation);
            }

            groups.Add(new ErrorEventGroup(group.Key.ProcessorId, category, group.Key.Code, members.Count, members[0].OccurredAt, members[^1].OccurredAt, ids, factId));
        }

        var shown = ListOf.Capped(groups, cap, "Error event groups", out var groupTruncation);
        if (groupTruncation is not null)
        {
            truncations.Add(groupTruncation);
        }

        return new ToolResult.Ok(new ErrorEventSummary(shown), evidence, truncations);
    }
}
