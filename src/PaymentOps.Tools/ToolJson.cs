using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentOps.Tools;

/// <summary>How tool results are written for the model: snake_case, enums as words, nothing indented.</summary>
public static class ToolJson
{
    public static JsonSerializerOptions Options { get; } = ReadOnly(new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },

        // Read by the model, never rendered as HTML, so markers like <untrusted-data> stay legible.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>
    /// How the model's tool arguments are read: as <see cref="Options"/>, but an argument the request doesn't declare
    /// is an error, so nothing the tool ignores can vary between otherwise identical calls.
    /// </summary>
    public static JsonSerializerOptions Requests { get; } = ReadOnly(new(Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });

    /// <summary>An enum value as the model reads it, such as <c>file_transfer_timeout</c>; Domain enums stay out of tool output.</summary>
    public static string Word(Enum value) => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static string Serialize(ToolResult result) => result switch
    {
        ToolResult.Ok ok => JsonSerializer.Serialize(new { Status = "ok", Result = ok.Body }, Options),
        ToolResult.Failed failed => JsonSerializer.Serialize(new { Status = "error", failed.Code, failed.Message }, Options),
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    // Shared by every investigation at once: finished here, with its resolver, so no caller (schema generation
    // included) can change it while another is reading it.
    private static JsonSerializerOptions ReadOnly(JsonSerializerOptions options)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
