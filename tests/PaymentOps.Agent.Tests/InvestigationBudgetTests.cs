using System.Net;
using System.Text;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Agent.Tests.Telemetry;
using PaymentOps.Tools;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// One budget per investigation, checked before each model call: logical calls, and an estimated token allowance whose
/// first call is estimated from the request's own bytes (ADR 0023).
/// </summary>
public sealed class InvestigationBudgetTests
{
    private static readonly InvestigationLimits Limits = new();

    private static ChatResponse Answer(long? input = 100, long? output = 10) =>
        new(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            Usage = input is null && output is null ? null : new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
        };

    private static List<ChatMessage> Question(string text = "q") => [new(ChatRole.User, text)];

    private static ChatOptions Options(string instructions = "i") => new() { Instructions = instructions, MaxOutputTokens = Limits.MaxOutputTokens };

    private static (InvestigationBudget Budget, BudgetedChatClient Client) Budgeted(IChatClient inner, InvestigationLimits? limits = null, TimeProvider? time = null)
    {
        var budget = new InvestigationBudget(limits ?? Limits, time ?? TimeProvider.System);
        budget.StartAttempt();
        return (budget, new BudgetedChatClient(inner, budget));
    }

    [Fact]
    public void Byte_bound_estimate_counts_instructions_messages_and_tools()
    {
        var tool = AIFunctionFactory.Create(() => 1, "lookup", "Looks it up.");
        var options = new ChatOptions { Instructions = "abc", Tools = [tool] };

        var estimate = RequestSizeEstimate.FirstCall([new ChatMessage(ChatRole.User, "héllo")], options);

        var toolBytes = Encoding.UTF8.GetByteCount("lookup") + Encoding.UTF8.GetByteCount("Looks it up.") + Encoding.UTF8.GetByteCount(tool.JsonSchema.GetRawText());
        Assert.Equal(3 + 6 + toolBytes + RequestSizeEstimate.PerMessage + RequestSizeEstimate.PerTool + RequestSizeEstimate.Fixed, estimate);
    }

    [Fact]
    public void Tool_calls_and_results_count_their_arguments_and_results()
    {
        ChatMessage[] added =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "lookup", new Dictionary<string, object?> { ["id"] = "M-1" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "{\"ok\":true}")]),
        ];

        var estimate = RequestSizeEstimate.Appended(added);

        Assert.Equal(("c1lookup" + "{\"id\":\"M-1\"}" + "c1" + "{\"ok\":true}").Length + (2 * RequestSizeEstimate.PerMessage), estimate);
    }

    [Fact]
    public async Task A_request_that_cannot_be_estimated_is_not_made()
    {
        var inner = new ScriptedChatClient(_ => Answer());
        var (_, client) = Budgeted(inner);
        List<ChatMessage> messages = [new(ChatRole.User, [new DataContent(new byte[] { 1, 2, 3 }, "image/png")])];

        var refused = await Assert.ThrowsAsync<InvestigationBudgetExceededException>(() => client.GetResponseAsync(messages, Options()));

        Assert.Equal("estimate_unavailable", refused.Bound);
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task First_call_is_checked_against_its_byte_bound_estimate()
    {
        var inner = new ScriptedChatClient(_ => Answer());
        var (_, client) = Budgeted(inner, new InvestigationLimits { MaxInvestigationTokens = 3_000 });

        var refused = await Assert.ThrowsAsync<InvestigationBudgetExceededException>(() => client.GetResponseAsync(Question(), Options(new string('x', 1_000))));

        Assert.Equal("tokens", refused.Bound);
        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task A_call_whose_projection_exceeds_the_allowance_is_not_made()
    {
        var inner = new ScriptedChatClient(_ => Answer(60_000, 1_000), _ => Answer());
        var (budget, client) = Budgeted(inner);
        var messages = Question();
        await client.GetResponseAsync(messages, Options());

        // 61,000 used + 60,000 estimated input + 2,000 output > 120,000.
        var refused = await Assert.ThrowsAsync<InvestigationBudgetExceededException>(() => client.GetResponseAsync([.. messages, new ChatMessage(ChatRole.Assistant, "ok")], Options()));

        Assert.Equal("tokens", refused.Bound);
        Assert.Single(inner.Requests);
        Assert.Equal(61_000, budget.TokensUsed);
    }

    [Fact]
    public async Task Later_estimate_is_previous_input_plus_bytes_added()
    {
        var (budget, client) = Budgeted(new ScriptedChatClient(_ => Answer(100, 10), _ => Answer(150, 10)));
        var messages = Question();
        await client.GetResponseAsync(messages, Options());

        await client.GetResponseAsync([.. messages, new ChatMessage(ChatRole.Assistant, "xyz")], Options());

        Assert.Equal(100 + 3 + RequestSizeEstimate.PerMessage, budget.LastEstimate);
        Assert.Equal(270, budget.TokensUsed);
    }

    [Fact]
    public async Task Missing_usage_is_charged_its_projection_and_marks_usage_incomplete()
    {
        using var usage = TurnUsage.Begin();
        var (budget, client) = Budgeted(new UsageCountingChatClient(new ScriptedChatClient(_ => Answer(null, null)), "chat-small"));

        await client.GetResponseAsync(Question(), Options());

        Assert.Equal(budget.LastEstimate + Limits.MaxOutputTokens, budget.TokensUsed);
        Assert.False(usage.Complete);
    }

    [Fact]
    public async Task After_missing_usage_the_next_estimate_starts_from_the_charge()
    {
        var (budget, client) = Budgeted(new ScriptedChatClient(_ => Answer(null, null), _ => Answer()));
        var messages = Question();
        await client.GetResponseAsync(messages, Options());
        var first = budget.LastEstimate;

        await client.GetResponseAsync([.. messages, new ChatMessage(ChatRole.Assistant, "xyz")], Options());

        Assert.Equal(first + 3 + RequestSizeEstimate.PerMessage, budget.LastEstimate);
    }

    [Fact]
    public async Task Measured_input_above_its_estimate_is_counted()
    {
        var (budget, client) = Budgeted(new ScriptedChatClient(_ => Answer(5_000, 10)));

        await client.GetResponseAsync(Question(), Options());

        Assert.Equal(1, budget.EstimateExceeded);
    }

    [Fact]
    public async Task Seventeenth_logical_call_is_not_made()
    {
        var inner = new ScriptedChatClient([.. Enumerable.Repeat<Func<IReadOnlyList<ChatMessage>, ChatResponse>>(_ => Answer(10, 1), 17)]);
        var (budget, client) = Budgeted(inner);
        for (var i = 0; i < 16; i++)
        {
            await client.GetResponseAsync(Question(), Options());
        }

        var refused = await Assert.ThrowsAsync<InvestigationBudgetExceededException>(() => client.GetResponseAsync(Question(), Options()));

        Assert.Equal("model_calls", refused.Bound);
        Assert.Equal(16, budget.LogicalCalls);
        Assert.Equal(16, inner.Requests.Count);
    }

    [Fact]
    public async Task Http_retries_are_not_logical_calls()
    {
        using var usage = TurnUsage.Begin();
        var model = ModelClient.Create(new Uri("https://foundry.invalid/"), "https://cognitiveservices.azure.com/.default", new StaticTokenCredential(), "chat-small",
            new UsageAccountingTests.ScriptedModel(HttpStatusCode.TooManyRequests, HttpStatusCode.OK));
        var (budget, client) = Budgeted(model);

        await client.GetResponseAsync(Question(), Options());

        Assert.Equal(1, budget.LogicalCalls);
        Assert.Equal(2, usage.HttpRequests["model"]);
    }

    [Fact]
    public void A_fresh_budget_allows_escalation() =>
        Assert.Null(new InvestigationBudget(Limits, TimeProvider.System).RefuseEscalation(1_000));

    [Theory]
    [InlineData(40.0, null)]
    [InlineData(40.1, "time")]
    public void Escalation_needs_twenty_seconds_left(double elapsed, string? refused)
    {
        var time = new FakeTimeProvider();
        var budget = new InvestigationBudget(Limits, time);
        time.Advance(TimeSpan.FromSeconds(elapsed));

        Assert.Equal(refused, budget.RefuseEscalation(1_000));
    }

    [Theory]
    [InlineData(8, null)]
    [InlineData(9, "tool_calls")]
    public void Escalation_needs_two_tool_calls_left(int used, string? refused)
    {
        var budget = new InvestigationBudget(Limits, TimeProvider.System);
        for (var i = 0; i < used; i++)
        {
            Assert.True(budget.TryTakeToolCall());
        }

        Assert.Equal(refused, budget.RefuseEscalation(1_000));
    }

    [Theory]
    [InlineData(8, null)]
    [InlineData(9, "model_calls")]
    public async Task Escalation_needs_eight_logical_calls_left(int used, string? refused)
    {
        var (budget, client) = Budgeted(new ScriptedChatClient([.. Enumerable.Repeat<Func<IReadOnlyList<ChatMessage>, ChatResponse>>(_ => Answer(10, 1), used)]));
        for (var i = 0; i < used; i++)
        {
            await client.GetResponseAsync(Question(), Options());
        }

        Assert.Equal(refused, budget.RefuseEscalation(1_000));
    }

    [Theory]
    [InlineData(60_000, null)]
    [InlineData(60_001, "tokens")]
    public async Task Escalation_needs_sixty_thousand_tokens_left(long used, string? refused)
    {
        var (budget, client) = Budgeted(new ScriptedChatClient(_ => Answer(used, 0)));
        await client.GetResponseAsync(Question(), Options());

        Assert.Equal(refused, budget.RefuseEscalation(1_000));
    }

    [Fact]
    public async Task A_first_call_that_does_not_fit_the_remaining_allowance_is_not_made()
    {
        var (budget, client) = Budgeted(new ScriptedChatClient(_ => Answer(60_000, 0)));
        await client.GetResponseAsync(Question(), Options());

        // 60,000 used + 58,001 estimated + 2,000 output > 120,000.
        Assert.Equal("first_call", budget.RefuseEscalation(58_001));
        Assert.Null(budget.RefuseEscalation(58_000));
    }

    [Fact]
    public void An_escalation_whose_first_call_cannot_be_estimated_is_refused() =>
        Assert.Equal("estimate_unavailable", new InvestigationBudget(Limits, TimeProvider.System).RefuseEscalation(null));

    [Fact]
    public void Tool_calls_stop_at_the_limit()
    {
        var budget = new InvestigationBudget(Limits, TimeProvider.System);
        for (var i = 0; i < Limits.MaxToolCalls; i++)
        {
            Assert.True(budget.TryTakeToolCall());
        }

        Assert.False(budget.TryTakeToolCall());
    }

    // The shipped instructions and tools, with the longest question, leave most of the allowance for the investigation.
    [Fact]
    public void The_shipped_first_request_uses_a_small_part_of_the_allowance()
    {
        using var fixture = new AgentFixture();
        var guard = new ToolGuard(new TurnState(), Limits, TimeProvider.System, TestUser.Reader, null);
        string[] names = [ToolNames.MerchantProfile, ToolNames.SettlementBatches, ToolNames.ProcessorStatus, ToolNames.ErrorEvents, ToolNames.KnowledgeSearch];
        var options = new ChatOptions { Instructions = fixture.FixedPrompt.Instructions(), Tools = [.. names.Select(n => guard.Wrap(fixture.Tool(n)))] };

        var estimate = RequestSizeEstimate.FirstCall([new ChatMessage(ChatRole.User, new string('x', Limits.MaxQuestionLength))], options);

        Assert.InRange(estimate!.Value, 1, Limits.MaxInvestigationTokens / 6);
    }
}
