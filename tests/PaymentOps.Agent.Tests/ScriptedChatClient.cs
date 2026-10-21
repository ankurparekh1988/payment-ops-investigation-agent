using System.Text.Json;

using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// Stands in for the model under the real function-invoking layer: answers each request with the next scripted
/// response and records what each request offered, so tests see exactly what the loop sent and when it stopped.
/// </summary>
internal sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] script) : IChatClient
{
    private int _next;

    public List<(IReadOnlyList<string> Tools, ChatToolMode? Mode, IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    public static ChatResponse Call(string name, object arguments, string? callId = null) =>
        Calls((name, arguments, callId));

    public static ChatResponse Calls(params (string Name, object Arguments, string? CallId)[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, [.. calls.Select((c, i) => new FunctionCallContent(c.CallId ?? $"call-{Guid.NewGuid():N}", c.Name, ToArguments(c.Arguments)))]))
        {
            FinishReason = ChatFinishReason.ToolCalls,
        };

    public static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop };

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Requests.Add(([.. options?.Tools?.Select(t => t.Name) ?? []], options?.ToolMode, list, options));
        if (_next >= script.Length)
        {
            throw new InvalidOperationException($"The script has {script.Length} responses; request {_next + 1} wasn't expected.");
        }

        return Task.FromResult(script[_next++](list));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    /// <summary>The tool results the loop sent back to the model, in order.</summary>
    public static IReadOnlyList<string> ToolResults(IEnumerable<ChatMessage> messages) =>
        [.. messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString() ?? string.Empty)];

    private static Dictionary<string, object?> ToArguments(object arguments) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments))!;
}
