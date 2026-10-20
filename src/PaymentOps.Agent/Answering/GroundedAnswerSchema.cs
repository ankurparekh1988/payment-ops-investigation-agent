using System.Text.Json;

using OpenAI.Chat;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// The response shape, sent strict with every request. Structured Outputs can't express minLength,
/// minItems or uniqueItems, so those rules are enforced by <see cref="GroundedAnswerValidator"/>.
/// </summary>
internal static class GroundedAnswerSchema
{
    public const string Name = "grounded_answer";

    private const string Text = """
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["answered", "refused"] },
            "answer_markdown": { "type": "string" },
            "citation_ids": { "type": "array", "items": { "type": "string" } },
            "refusal_reason": { "type": "string" }
          },
          "required": ["status", "answer_markdown", "citation_ids", "refusal_reason"],
          "additionalProperties": false
        }
        """;

    public static JsonElement Schema { get; } = JsonDocument.Parse(Text).RootElement.Clone();

    public static ChatResponseFormat Format { get; } =
        ChatResponseFormat.CreateJsonSchemaFormat(Name, BinaryData.FromString(Text), jsonSchemaIsStrict: true);
}
