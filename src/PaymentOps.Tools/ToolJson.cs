using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentOps.Tools;

/// <summary>How tool results are written for the model: snake_case, enums as words, nothing indented.</summary>
public static class ToolJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>An enum value as the model reads it, such as <c>file_transfer_timeout</c>; Domain enums stay out of tool output.</summary>
    public static string Word(Enum value) => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static string Serialize(ToolResult result) => result switch
    {
        ToolResult.Ok ok => JsonSerializer.Serialize(new { Status = "ok", Result = ok.Body }, Options),
        ToolResult.Failed failed => JsonSerializer.Serialize(new { Status = "error", failed.Code, failed.Message }, Options),
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };
}
