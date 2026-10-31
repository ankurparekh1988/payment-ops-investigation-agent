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
public sealed class ErrorEventsTool(IErrorEventData events, IInvestigationClock clock, IOptions<ToolOptions> options)
    : InvestigationTool<ErrorEventsRequest>(options)
{
    public override string Name => ToolNames.ErrorEvents;

    public override string Description =>
        "Errors our platform recorded with processors from start_date to end_date, both included, for a processor_id, a merchant_id or both, " +
        "grouped by category and code with a count and first and last times. Events not tied to a merchant appear only when filtering by processor.";

    public override string Source => DataSources.ErrorEvents;

    protected override async Task<ToolResult> RunCoreAsync(ErrorEventsRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        // Models often send an unused optional id as an empty string; that's not given, not invalid.
        var processorId = string.IsNullOrWhiteSpace(request.ProcessorId) ? null : ToolArguments.Id(request.ProcessorId, "processor_id");
        var merchantId = string.IsNullOrWhiteSpace(request.MerchantId) ? null : ToolArguments.Id(request.MerchantId, "merchant_id");
        if (processorId is null && merchantId is null)
        {
            return new ToolResult.Failed(ToolErrorCode.InvalidArgument, "Give a processor_id, a merchant_id, or both.");
        }
        var range = ToolArguments.Range(request.StartDate, request.EndDate, clock.Today);
        var found = await ReadAsync(
            token => events.GetEventsAsync(new ErrorEventQuery(processorId, merchantId, range.FromUtc, range.ToUtcExclusive), token),
            DataSources.ErrorEvents,
            cancellationToken);

        var evidence = Collect();
        var groups = found.GroupBy(e => (e.ProcessorId, e.Category, e.Code)).Select(group =>
        {
            var members = group.ToList();
            var category = ToolJson.Word(group.Key.Category);
            var eventIds = members.Select(e => evidence.Record(EvidenceIds.Event(e.Id), $"Error event {e.Id}")).ToList();
            var factId = evidence.Derive(
                EvidenceIds.EventGroup(group.Key.ProcessorId, merchantId, category, group.Key.Code, range),
                string.Create(CultureInfo.InvariantCulture, $"{members.Count} {category} events from {group.Key.ProcessorId}"),
                eventIds);
            // Codes are the processor's, not this system's: screened even when they look like codes.
            return new ErrorEventGroup(
                group.Key.ProcessorId, category, UntrustedText.Label(group.Key.Code), members.Count, members[0].OccurredAt, members[^1].OccurredAt,
                evidence.Cap<string>([.. members.Select(e => e.Id)], $"{Sentence(category)} events from {group.Key.ProcessorId}"), factId);
        }).ToList();

        return evidence.Ok(new ErrorEventSummary(evidence.Cap(groups, "Error event groups")));
    }

    // "file_transfer_timeout" as people read it: "File transfer timeout".
    private static string Sentence(string word) => string.Concat(char.ToUpperInvariant(word[0]).ToString(), word[1..].Replace('_', ' '));
}
