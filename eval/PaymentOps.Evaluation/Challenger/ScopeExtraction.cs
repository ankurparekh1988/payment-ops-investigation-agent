using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;

using PaymentOps.Data.Demo;
using PaymentOps.Evaluation.Prompts;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Challenger;

/// <summary>A processor the extraction may name, by id and display name.</summary>
internal sealed record KnownProcessor(string Id, string Name)
{
    /// <summary>The processors in the demo dataset the app runs on, read from the same embedded copy.</summary>
    public static IReadOnlyList<KnownProcessor> FromDemoData()
    {
        using var stream = typeof(DemoDataServiceCollectionExtensions).Assembly.GetManifestResourceStream("data/datasets/default/dataset.json")
            ?? throw new InvalidOperationException("The demo dataset isn't embedded.");
        using var json = JsonDocument.Parse(stream);
        return [.. json.RootElement.GetProperty("processors").EnumerateArray().Select(p => new KnownProcessor(p.GetProperty("id").GetString()!, p.GetProperty("name").GetString()!))];
    }
}

/// <summary>What the question asks about, as the extraction call read it; dates as written, checked later.</summary>
internal sealed record ExtractedScope(IReadOnlyList<string> MerchantIds, IReadOnlyList<string> ProcessorIds, string? StartDate, string? EndDate);

/// <summary>
/// The challenger's one planning step: a single structured-output call, with no tools, that reads the merchants,
/// processors and dates out of the question. It sees only the question, today's date and the known processors.
/// </summary>
internal static class ScopeExtraction
{
    public const int MaxMerchants = 3;
    public const int DefaultWindowDays = 7;

    public static EvaluationPrompt Prompt { get; } = EvaluationPrompt.Load("prompts/scope-extraction.md");

    private static readonly JsonSerializerOptions Strict = new(ToolJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    public static async Task<ExtractedScope> ExtractAsync(IChatClient model, string question, DateOnly today, IReadOnlyList<KnownProcessor> processors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(processors);

        var known = string.Join(", ", processors.Select(p => $"{p.Id} ({p.Name})"));
        var response = await model.GetResponseAsync<ExtractedScope>(
            [
                new ChatMessage(ChatRole.System, $"{Prompt.Body}\n\nToday is {today:yyyy-MM-dd}. Known processors: {known}."),
                new ChatMessage(ChatRole.User, question),
            ],
            Strict,
            new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } },
            cancellationToken: cancellationToken);

        // Unreadable output scopes nothing, so only knowledge is searched: the challenger's loss, never a crash.
        var scope = response.TryGetResult(out var result) && result is not null ? result : new ExtractedScope([], [], null, null);
        return scope with
        {
            MerchantIds = [.. scope.MerchantIds.Where(id => ToolArguments.TryId(id, "merchant_id", out _, out _)).Distinct(StringComparer.Ordinal).Take(MaxMerchants)],
            ProcessorIds = [.. scope.ProcessorIds.Where(id => processors.Any(p => p.Id == id)).Distinct(StringComparer.Ordinal)],
        };
    }

    /// <summary>The window every dated call uses: at most 31 days, ending no later than today; the last 7 days when none was given.</summary>
    public static (DateOnly Start, DateOnly End) Window(ExtractedScope scope, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!ToolArguments.TryDate(scope.StartDate, out var start) || !ToolArguments.TryDate(scope.EndDate, out var end) || start > end)
        {
            return (today.AddDays(1 - DefaultWindowDays), today);
        }

        end = end > today ? today : end;
        var earliest = end.AddDays(1 - ToolArguments.MaxRangeDays);
        start = start < earliest ? earliest : start > end ? end : start;
        return (start, end);
    }
}
