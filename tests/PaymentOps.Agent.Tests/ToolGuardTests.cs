using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Investigating;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Tests;

public sealed class ToolGuardTests
{
    private readonly TurnState _turn = new();
    private readonly FakeTimeProvider _time = new();

    private ToolGuard Guard() => new(_turn, new InvestigationLimits(), _time, TestUser.Reader);

    // The real fail-closed function layer over a scripted model, so termination is the framework's own.
    private static async Task<ScriptedChatClient> RunAsync(IReadOnlyList<AITool> tools, params ChatResponse[] responses)
    {
        var script = responses.Select(r => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => r)).Append(_ => ScriptedChatClient.Text("done")).ToArray();
        var model = new ScriptedChatClient(script);
        using var client = InvestigationPipeline.FunctionInvoking(model, new InvestigationLimits());
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], new ChatOptions { Tools = [.. tools] });
        return model;
    }

    [Fact]
    public async Task The_eleventh_call_stops_the_turn_without_running()
    {
        var tool = new StubTool();
        var calls = Enumerable.Range(0, 11).Select(i => ScriptedChatClient.Call(tool.Name, new { merchant_id = $"M-{i}" })).ToArray();

        var model = await RunAsync([Guard().Wrap(tool)], calls);

        Assert.Equal(10, tool.Runs);
        Assert.True(_turn.Stopped);
        Assert.Contains(_turn.Gaps, g => g.Kind == ObservedGapKind.LimitReached);
        Assert.Equal(11, model.Requests.Count);
    }

    [Fact]
    public async Task A_third_identical_call_stops_the_turn()
    {
        var tool = new StubTool();
        var calls = Enumerable.Range(0, 3).Select(_ => ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042" })).ToArray();

        await RunAsync([Guard().Wrap(tool)], calls);

        Assert.Equal(2, tool.Runs);
        Assert.True(_turn.Stopped);
    }

    // Ids are opaque and matched exactly, so a different case is a different call.
    [Fact]
    public async Task Calls_differing_only_in_id_case_are_not_repeats()
    {
        var tool = new StubTool();

        await RunAsync(
            [Guard().Wrap(tool)],
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042" }),
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "m-1042" }),
            ScriptedChatClient.Call(tool.Name, new { merchant_id = " M-1042 " }));

        Assert.Equal(3, tool.Runs);
        Assert.False(_turn.Stopped);
    }

    [Fact]
    public async Task Surrounding_whitespace_doesn_t_make_a_new_call()
    {
        var tool = new StubTool();

        await RunAsync(
            [Guard().Wrap(tool)],
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042" }),
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042 " }),
            ScriptedChatClient.Call(tool.Name, new { merchant_id = " M-1042" }));

        Assert.Equal(2, tool.Runs);
        Assert.True(_turn.Stopped);
    }

    // The server reads arguments strictly whatever the wire schema says: an argument the tool doesn't declare is an error.
    [Fact]
    public async Task An_unknown_argument_is_invalid_and_the_tool_doesn_t_run()
    {
        var tool = new StubTool();

        var result = (string?)await Guard().Wrap(tool).InvokeAsync(new AIFunctionArguments { ["merchant_id"] = "M-1042", ["note"] = "again" });

        Assert.Contains("\"code\":\"invalid_argument\"", result, StringComparison.Ordinal);
        Assert.Equal(0, tool.Runs);
    }

    [Fact]
    public async Task Varying_an_unknown_argument_doesn_t_get_past_the_repeat_limit()
    {
        var tool = new StubTool();
        var calls = Enumerable.Range(0, 4).Select(i => ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042", note = $"try {i}" }));

        await RunAsync([Guard().Wrap(tool)], [ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042" }), ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042" }), .. calls]);

        Assert.Equal(2, tool.Runs);
        Assert.True(_turn.Stopped);
    }

    // Repeats are keyed on what the tool would receive, so spelling "not given" another way is the same call.
    [Fact]
    public async Task A_null_or_blank_optional_argument_is_the_same_call_as_leaving_it_out()
    {
        var tool = new StubTool();

        await RunAsync(
            [Guard().Wrap(tool)],
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042" }),
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042", query = (string?)null }),
            ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1042", query = " " }));

        Assert.Equal(2, tool.Runs);
        Assert.True(_turn.Stopped);
    }

    [Fact]
    public async Task A_third_argument_correction_stops_the_turn()
    {
        var tool = new StubTool { Result = new ToolResult.Failed(ToolErrorCode.RangeTooLarge, "The range can be at most 31 days.") };
        var calls = Enumerable.Range(0, 3).Select(i => ScriptedChatClient.Call(tool.Name, new { merchant_id = $"M-{i}" })).ToArray();

        await RunAsync([Guard().Wrap(tool)], calls);

        Assert.True(_turn.Stopped);
    }

    [Fact]
    public async Task A_tool_slower_than_its_timeout_is_reported_as_a_timeout_and_a_gap()
    {
        var tool = new StubTool { Delay = TimeSpan.FromSeconds(30) };
        var guard = Guard();
        var function = guard.Wrap(tool);

        var pending = function.InvokeAsync(new AIFunctionArguments { ["merchant_id"] = "M-1042" }).AsTask();
        _time.Advance(TimeSpan.FromSeconds(5));
        var result = (string?)await pending;

        Assert.Contains("\"code\":\"timeout\"", result, StringComparison.Ordinal);
        Assert.Contains(_turn.Gaps, g => g.Kind == ObservedGapKind.Timeout && g.Wording == "Merchant data didn't respond in time.");
    }

    [Fact]
    public async Task The_callers_cancellation_isn_t_mistaken_for_a_timeout()
    {
        var tool = new StubTool { Delay = TimeSpan.FromSeconds(30) };
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guard().Wrap(tool).InvokeAsync(new AIFunctionArguments { ["merchant_id"] = "M-1" }, cancelled.Token).AsTask());
        Assert.Empty(_turn.Gaps);
    }

    // Fail closed: no exception text reaches the model, and the framework makes no recovery request.
    [Fact]
    public async Task An_unexpected_exception_ends_the_turn()
    {
        var tool = new StubTool { Throws = new InvalidOperationException("secret detail") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync([Guard().Wrap(tool)], ScriptedChatClient.Call(tool.Name, new { merchant_id = "M-1" })));

        Assert.Equal("secret detail", error.Message);
    }

    [Fact]
    public async Task An_unknown_tool_call_ends_the_loop()
    {
        var tool = new StubTool();

        var model = await RunAsync([Guard().Wrap(tool)], ScriptedChatClient.Call("delete_everything", new { merchant_id = "M-1" }));

        Assert.Single(model.Requests);
        Assert.Equal(0, tool.Runs);
    }

    [Fact]
    public async Task Evidence_and_truncations_are_recorded_in_the_turn()
    {
        var tool = new StubTool
        {
            Result = new ToolResult.Ok(new { id = "M-1" }, [new RecordEvidence("merchant:M-1", "Merchant One")], [new Truncation("Payments", 20, 57)]),
        };

        await Guard().Wrap(tool).InvokeAsync(new AIFunctionArguments { ["merchant_id"] = "M-1" });

        Assert.True(_turn.TryGetEvidence("merchant:M-1", out _));
        Assert.Contains(_turn.Gaps, g => g.Wording == "Payments: only the first 20 of 57 were shown.");
    }

    [Fact]
    public async Task An_unavailable_source_is_a_gap_named_for_the_source()
    {
        var tool = new StubTool { Result = new ToolResult.Failed(ToolErrorCode.Unavailable, "Settlement data is unavailable.", "Settlement data") };

        await Guard().Wrap(tool).InvokeAsync(new AIFunctionArguments { ["merchant_id"] = "M-1" });

        Assert.Contains(_turn.Gaps, g => g.Kind == ObservedGapKind.Unavailable && g.Wording == "Settlement data was unavailable.");
    }

    // Citation ids belong to the turn: the same section keeps its id across searches.
    [Fact]
    public async Task Knowledge_sections_are_numbered_across_searches_in_the_turn()
    {
        static RetrievedSection Section(string key) => new(key, "d", $"Title {key}", "Path", "Content", "General", null);
        var first = new StubTool(ToolNames.KnowledgeSearch) { Result = new ToolResult.Ok(new KnowledgeSearchResult([Section("a"), Section("b")]), [], []) };
        var second = new StubTool(ToolNames.KnowledgeSearch) { Result = new ToolResult.Ok(new KnowledgeSearchResult([Section("b"), Section("c")]), [], []) };
        var guard = Guard();

        await guard.Wrap(first).InvokeAsync(new AIFunctionArguments { ["query"] = "one" });
        var result = (string?)await guard.Wrap(second).InvokeAsync(new AIFunctionArguments { ["query"] = "two" });

        Assert.Contains("\"citation_id\":\"S2\"", result, StringComparison.Ordinal);
        Assert.Contains("\"citation_id\":\"S3\"", result, StringComparison.Ordinal);
        Assert.Contains("<untrusted-data>Content</untrusted-data>", result, StringComparison.Ordinal);
        Assert.True(_turn.TryGetSection("S3", out var third));
        Assert.Equal("c", third.Key);
    }

    private sealed record StubRequest(string? MerchantId, string? Query);

    private sealed class StubTool(string name = ToolNames.MerchantProfile) : IInvestigationTool
    {
        public int Runs { get; private set; }

        public ToolResult Result { get; init; } = new ToolResult.Ok(new { ok = true }, [], []);

        public TimeSpan Delay { get; init; }

        public Exception? Throws { get; init; }

        public string Name => name;

        public string Description => "Stub.";

        public Type RequestType => typeof(StubRequest);

        public string Source => DataSources.Merchants;

        public async Task<ToolResult> RunAsync(object request, IUserContext user, CancellationToken cancellationToken)
        {
            Runs++;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return Throws is null ? Result : throw Throws;
        }
    }
}
