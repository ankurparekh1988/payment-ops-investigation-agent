using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Citations;
using PaymentOps.Agent.Contracts;

using NotAnswered = PaymentOps.Agent.Contracts.InvestigationResult.NotAnswered;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// Decides whether a model response may be shown. Every failure fails the whole answer: a bad
/// citation is never dropped so the rest can be shown.
/// </summary>
internal static class GroundedAnswerValidator
{
    private static readonly JsonSerializerOptions Strict = StrictJson.Create();

    public static InvestigationResult Validate(ChatResponse response, EvidenceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.RawRepresentation is OpenAI.Chat.ChatCompletion { Refusal.Length: > 0 })
        {
            return new NotAnswered(NotAnsweredReason.AnswerBlocked);
        }

        if (response.FinishReason != ChatFinishReason.Stop)
        {
            return new NotAnswered(
                response.FinishReason == ChatFinishReason.Length ? NotAnsweredReason.AnswerIncomplete
                : response.FinishReason == ChatFinishReason.ContentFilter ? NotAnsweredReason.AnswerBlocked
                : NotAnsweredReason.AnswerUnverifiable);
        }

        if (string.IsNullOrWhiteSpace(response.Text) || Parse(response.Text) is not { } payload)
        {
            return new NotAnswered(NotAnsweredReason.AnswerUnverifiable);
        }

        return payload.Status switch
        {
            "refused" when string.IsNullOrWhiteSpace(payload.AnswerMarkdown) && payload.CitationIds.Count == 0 && !string.IsNullOrWhiteSpace(payload.RefusalReason)
                => new NotAnswered(NotAnsweredReason.InsufficientEvidence),
            "answered" when !string.IsNullOrWhiteSpace(payload.AnswerMarkdown) && payload.CitationIds.Count > 0 && string.IsNullOrWhiteSpace(payload.RefusalReason)
                => CheckProvenance(payload, registry),
            _ => new NotAnswered(NotAnsweredReason.AnswerUnverifiable),
        };
    }

    private static InvestigationResult CheckProvenance(Payload payload, EvidenceRegistry registry)
    {
        var scan = CitationScanner.Scan(payload.AnswerMarkdown);
        var markers = scan.Citations.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var declared = payload.CitationIds.ToHashSet(StringComparer.Ordinal);

        var valid = !scan.HasMalformedMarker
            && declared.Count == payload.CitationIds.Count
            && markers.SetEquals(declared)
            && declared.All(registry.Contains);

        return valid
            ? new InvestigationResult.Answered(payload.AnswerMarkdown, [.. payload.CitationIds.Select(registry.CitationFor)])
            : new NotAnswered(NotAnsweredReason.AnswerUnverifiable);
    }

    private static Payload? Parse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<Payload>(text, Strict);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Payload(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("answer_markdown")] string AnswerMarkdown,
        [property: JsonPropertyName("citation_ids")] IReadOnlyList<string> CitationIds,
        [property: JsonPropertyName("refusal_reason")] string RefusalReason);
}
