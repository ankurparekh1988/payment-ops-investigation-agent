using System.Text;
using System.Text.Json;

using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// An upper bound on a request's input tokens, computed before it's sent: its UTF-8 bytes plus framing. No token in the
/// tokenizer encodes fewer than one byte, so the bytes bound the text's tokens; the framing allowances cover the
/// service's own wrapping. Null when the request carries something this can't measure, and then it isn't sent.
/// </summary>
internal static class RequestSizeEstimate
{
    public const int PerMessage = 16;
    public const int PerTool = 32;
    public const int Fixed = 256;

    /// <summary>A request with nothing measured before it: everything it sends.</summary>
    public static int? FirstCall(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var total = (long)Fixed + Bytes(options?.Instructions);
        foreach (var tool in options?.Tools ?? [])
        {
            if (tool is not AIFunctionDeclaration function)
            {
                return null;
            }

            total += Bytes(function.Name) + Bytes(function.Description) + Bytes(function.JsonSchema.GetRawText()) + PerTool;
        }

        return Appended(messages) is { } added ? Clamp(total + added) : null;
    }

    /// <summary>Messages added to a conversation since its last measured request.</summary>
    public static int? Appended(IEnumerable<ChatMessage> added)
    {
        long total = 0;
        foreach (var message in added)
        {
            total += PerMessage;
            foreach (var content in message.Contents)
            {
                if (Bytes(content) is not { } bytes)
                {
                    return null;
                }

                total += bytes;
            }
        }

        return Clamp(total);
    }

    private static long? Bytes(AIContent content)
    {
        try
        {
            return content switch
            {
                TextContent text => Bytes(text.Text),
                TextReasoningContent reasoning => Bytes(reasoning.Text),
                FunctionCallContent call => Bytes(call.CallId) + Bytes(call.Name) + Bytes(JsonSerializer.Serialize(call.Arguments)),
                FunctionResultContent result => Bytes(result.CallId) + Bytes(result.Result as string ?? JsonSerializer.Serialize(result.Result)),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static long Bytes(string? text) => text is null ? 0 : Encoding.UTF8.GetByteCount(text);

    private static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);
}
