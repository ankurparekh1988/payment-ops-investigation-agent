using System.Globalization;

using PaymentOps.Tools;
using PaymentOps.Tools.ErrorEvents;
using PaymentOps.Tools.Knowledge;
using PaymentOps.Tools.Merchants;
using PaymentOps.Tools.Processors;
using PaymentOps.Tools.Settlement;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// A timeline label from the parsed request: the tool's name plus only the ids and dates that pass validation.
/// Free text such as a search query is never shown, so a label can't carry injected text.
/// </summary>
internal static class ProgressLabels
{
    private const int MaxLength = 80;

    public static string For(string tool, object? request)
    {
        IEnumerable<string?> parts = request switch
        {
            MerchantProfileRequest r => [Id(r.MerchantId)],
            SettlementBatchesRequest r => [Id(r.MerchantId), Dates(r.StartDate, r.EndDate)],
            ProcessorStatusRequest r => [Id(r.ProcessorId), Dates(r.StartDate, r.EndDate)],
            ErrorEventsRequest r => [Id(r.ProcessorId), Id(r.MerchantId), Dates(r.StartDate, r.EndDate)],
            _ => [],
        };

        var label = string.Join(" · ", parts.Where(p => p is not null).Prepend(Name(tool)));
        return label.Length <= MaxLength ? label : string.Concat(label.AsSpan(0, MaxLength - 1), "…");
    }

    private static string Name(string tool) => tool switch
    {
        ToolNames.MerchantProfile => "Merchant profile",
        ToolNames.SettlementBatches => "Settlement batches",
        ToolNames.ProcessorStatus => "Processor status",
        ToolNames.ErrorEvents => "Error events",
        ToolNames.KnowledgeSearch => "Knowledge search",
        _ => tool,
    };

    private static string? Id(string? value) => ToolArguments.TryId(value, "id", out var id, out _) ? id : null;

    private static string? Dates(string? start, string? end) =>
        Date(start) is { } from && Date(end) is { } to
            ? string.Create(CultureInfo.InvariantCulture, $"{from:d MMM}–{to:d MMM}")
            : null;

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
