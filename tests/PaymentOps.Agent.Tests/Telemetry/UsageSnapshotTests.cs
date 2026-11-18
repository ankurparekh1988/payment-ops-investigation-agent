using System.Net;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests.Telemetry;

/// <summary>One frozen record of what an investigation used, taken before its usage scope ends: the one source for every reader.</summary>
public sealed class UsageSnapshotTests
{
    private sealed class RecordingSink : IInvestigationUsageSink
    {
        public List<(Guid Id, UsageSnapshot Usage, bool ScopeOpen)> Received { get; } = [];

        public void Completed(Guid investigationId, UsageSnapshot usage) => Received.Add((investigationId, usage, TurnUsage.Current is not null));
    }

    private static InvestigationService Service(AgentFixture fixture, IChatClient model, IInvestigationUsageSink sink)
    {
        var limits = Options.Create(new InvestigationLimits());
        return new InvestigationService(
            fixture.Runner(model),
            fixture.FixedPrompt,
            new SourceService(FakeKnowledgeLookup.Returning(), TestUser.Reader, limits, NullLogger<SourceService>.Instance),
            new Conversation(),
            Options.Create(new ConversationOptions()),
            limits,
            TestUser.Reader,
            new ProposalStore(TimeProvider.System, Options.Create(new ApprovalOptions()), AiReleaseLoader.Load(new EmbeddedAiReleaseSource())),
            usageSink: sink);
    }

    [Fact]
    public async Task Snapshot_is_taken_before_the_usage_scope_ends()
    {
        using var fixture = new AgentFixture();
        var sink = new RecordingSink();
        var model = new UsageCountingChatClient(
            new ScriptedChatClient(_ => Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }), _ => Call(ToolNames.SubmitAssessment, Submission())), "chat-blue");

        var result = await Service(fixture, model, sink).InvestigateAsync(RunnerExtensions.Question, null, CancellationToken.None);

        var (id, usage, scopeOpen) = Assert.Single(sink.Received);
        Assert.Equal(result.InvestigationId, id);
        Assert.True(scopeOpen);
        Assert.Null(TurnUsage.Current);
        Assert.Equal(new DeploymentUsage(2, 0, 2_000, 0, 200), usage.Deployments["chat-blue"]);
        Assert.Equal("off", usage.Routing);
        Assert.Equal("none", usage.Escalation);
        Assert.Equal(new AttemptRecord(ModelTier.Chat, null), Assert.Single(usage.Attempts));
    }

    [Fact]
    public void Snapshot_is_immutable()
    {
        using var usage = TurnUsage.Begin();
        TurnUsage.RecordTokens("chat-blue", 100, 10, 40);
        TurnUsage.RecordModelCall("chat-blue");

        var snapshot = usage.Snapshot();
        TurnUsage.RecordTokens("chat-blue", 100, 10, 40);
        TurnUsage.RecordModelCall("chat-blue");
        TurnUsage.RecordIncomplete("later");

        Assert.Equal(new DeploymentUsage(1, 0, 100, 40, 10), snapshot.Deployments["chat-blue"]);
        Assert.True(snapshot.Complete);
    }

    [Fact]
    public async Task Cached_input_and_logical_calls_are_kept_per_deployment()
    {
        using var usage = TurnUsage.Begin();
        var response = Text("ok");
        response.Usage = new UsageDetails { InputTokenCount = 500, CachedInputTokenCount = 300, OutputTokenCount = 20 };

        await new UsageCountingChatClient(new ScriptedChatClient(_ => response), "chat-small").GetResponseAsync("q");

        Assert.Equal(new DeploymentUsage(1, 0, 500, 300, 20), usage.Snapshot().Deployments["chat-small"]);
    }

    [Fact]
    public async Task Http_attempts_are_kept_per_deployment_apart_from_logical_calls()
    {
        using var usage = TurnUsage.Begin();
        var model = ModelClient.Create(new Uri("https://foundry.invalid/"), "https://cognitiveservices.azure.com/.default", new StaticTokenCredential(), "chat-small",
            new UsageAccountingTests.ScriptedModel(HttpStatusCode.TooManyRequests, HttpStatusCode.OK));

        await model.GetResponseAsync("q");

        var used = usage.Snapshot().Deployments["chat-small"];
        Assert.Equal((1, 2), (used.LogicalCalls, used.HttpAttempts));
    }

    [Fact]
    public void Cost_estimate_prices_cached_input_per_deployment()
    {
        using var usage = TurnUsage.Begin();
        TurnUsage.RecordTokens("chat-small", 1_000_000, 100_000, 400_000);
        var prices = new CostEstimateOptions
        {
            Currency = "USD",
            Deployments = [new() { Deployment = "chat-small", InputPerMillion = 0.20m, CachedInputPerMillion = 0.02m, OutputPerMillion = 1.25m }],
        };

        // 600,000 uncached at 0.20 + 400,000 cached at 0.02 + 100,000 output at 1.25.
        Assert.Equal(0.12m + 0.008m + 0.125m, CostEstimate.From(usage.Snapshot(), prices).Amount);
    }

    [Fact]
    public void Without_a_cached_price_cached_input_is_priced_as_input()
    {
        using var usage = TurnUsage.Begin();
        TurnUsage.RecordTokens("chat-small", 1_000_000, 0, 400_000);
        var prices = new CostEstimateOptions { Currency = "USD", Deployments = [new() { Deployment = "chat-small", InputPerMillion = 0.20m, OutputPerMillion = 1.25m }] };

        Assert.Equal(0.20m, CostEstimate.From(usage.Snapshot(), prices).Amount);
    }

    [Fact]
    public async Task A_measured_input_above_its_estimate_is_recorded()
    {
        using var usage = TurnUsage.Begin();
        using var budget = new InvestigationBudget(new InvestigationLimits(), TimeProvider.System);
        budget.StartAttempt();
        var response = Text("ok");
        response.Usage = new UsageDetails { InputTokenCount = 50_000, OutputTokenCount = 1 };

        await new BudgetedChatClient(new ScriptedChatClient(_ => response), budget).GetResponseAsync("q");

        Assert.Equal(1, usage.Snapshot().EstimateExceeded);
    }
}
