using System.Text.Json;

namespace PaymentOps.Agent.Investigating;

/// <summary>What the model submits through <c>submit_assessment</c>, read strictly; nothing here is trusted yet.</summary>
internal sealed record AssessmentSubmission(
    string Outcome,
    SubmittedStatement Summary,
    IReadOnlyList<SubmittedStatement> Claims,
    SubmittedStatement RootCause,
    string Confidence,
    IReadOnlyList<SubmittedNextStep> NextSteps,
    ProposedTicket? ProposedTicket)
{
    private static readonly JsonSerializerOptions Strict = StrictJson.Create(JsonNamingPolicy.SnakeCaseLower);

    public static AssessmentSubmission? Parse(JsonElement arguments)
    {
        try
        {
            return arguments.Deserialize<AssessmentSubmission>(Strict);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record SubmittedStatement(string Text, IReadOnlyList<string> EvidenceIds, IReadOnlyList<string> CitationIds);

internal sealed record SubmittedNextStep(string Text, IReadOnlyList<string> CitationIds);

/// <summary>An incident the model proposes raising. Dates are as written; the validator parses them.</summary>
internal sealed record ProposedTicket(
    string ProcessorId,
    IReadOnlyList<string> MerchantIds,
    string StartDate,
    string EndDate,
    string Title,
    string Summary,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> CitationIds);

/// <summary>The <c>submit_assessment</c> parameters as a strict JSON schema: every field required, nothing extra.</summary>
internal static class AssessmentSchema
{
    private const string StatementSchema = """
        { "type": "object", "additionalProperties": false, "required": ["text", "evidence_ids", "citation_ids"],
          "properties": {
            "text": { "type": "string", "description": "Plain sentences. Never write ids or citation markers in the text." },
            "evidence_ids": { "type": "array", "items": { "type": "string" }, "description": "Evidence ids exactly as tools returned them." },
            "citation_ids": { "type": "array", "items": { "type": "string" }, "description": "Citation ids such as S1 from search_knowledge." } } }
        """;

    public static JsonElement Parameters { get; } = JsonDocument.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["outcome", "summary", "claims", "root_cause", "confidence", "next_steps", "proposed_ticket"],
          "properties": {
            "outcome": { "type": "string", "enum": ["assessed", "incomplete", "escalated", "refused"] },
            "summary": {{StatementSchema}},
            "claims": { "type": "array", "items": {{StatementSchema}} },
            "root_cause": {{StatementSchema}},
            "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
            "next_steps": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["text", "citation_ids"],
              "properties": { "text": { "type": "string" }, "citation_ids": { "type": "array", "items": { "type": "string" } } } } },
            "proposed_ticket": {
              "type": ["object", "null"], "additionalProperties": false,
              "required": ["processor_id", "merchant_ids", "start_date", "end_date", "title", "summary", "evidence_ids", "citation_ids"],
              "description": "An incident to raise with one processor, or null when none should be raised.",
              "properties": {
                "processor_id": { "type": "string" },
                "merchant_ids": { "type": "array", "items": { "type": "string" } },
                "start_date": { "type": "string", "description": "YYYY-MM-DD, the first settlement date affected." },
                "end_date": { "type": "string", "description": "YYYY-MM-DD, the last settlement date affected." },
                "title": { "type": "string", "description": "At most 120 characters, plain text." },
                "summary": { "type": "string", "description": "At most 1,000 characters, plain text, no ids or card or account numbers." },
                "evidence_ids": { "type": "array", "items": { "type": "string" } },
                "citation_ids": { "type": "array", "items": { "type": "string" } } } }
          }
        }
        """).RootElement.Clone();
}
