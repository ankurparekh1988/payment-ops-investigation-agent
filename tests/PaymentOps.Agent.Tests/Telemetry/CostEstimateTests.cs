using PaymentOps.Agent.Telemetry;

namespace PaymentOps.Agent.Tests.Telemetry;

/// <summary>An estimate is given only when every price it needs is configured and the turn's usage is complete.</summary>
public sealed class CostEstimateTests
{
    private const string Chat = "chat-blue";
    private const string Screen = "screen";

    private static CostEstimateOptions Prices() => new()
    {
        Currency = "USD",
        Deployments =
        [
            new() { Deployment = Chat, InputPerMillion = 0.75m, OutputPerMillion = 4.50m },
            new() { Deployment = Screen, InputPerMillion = 0.20m, OutputPerMillion = 1.25m },
        ],
        ContentSafetyPerThousandRecords = 0.38m,
    };

    private static TurnUsage Used()
    {
        var usage = new TurnUsage();
        usage.AddTokens(Chat, 1_000_000, 100_000);
        usage.AddTokens(Screen, 200_000, 10_000);
        usage.AddRecords("prompt_shields", 600);
        usage.AddRecords("text_analysis", 400);
        return usage;
    }

    [Fact]
    public void Cost_is_estimated_from_configured_prices()
    {
        var estimate = CostEstimate.From(Used(), Prices());

        // 0.75 + 0.45 chat, 0.04 + 0.0125 screen, 1,000 records at 0.38 per thousand.
        Assert.Equal(1.6325m, estimate.Amount);
        Assert.Equal("USD", estimate.Currency);
        Assert.True(estimate.Complete);
        Assert.Null(estimate.Unavailable);
    }

    [Fact]
    public void No_currency_gives_no_estimate()
    {
        var estimate = CostEstimate.From(Used(), new CostEstimateOptions());

        Assert.Null(estimate.Amount);
        Assert.Equal("no prices", estimate.Unavailable);
    }

    [Fact]
    public void An_unpriced_deployment_gives_no_estimate()
    {
        var usage = Used();
        usage.AddTokens("judge", 10, 1);

        var estimate = CostEstimate.From(usage, Prices());

        Assert.Null(estimate.Amount);
        Assert.Equal("unpriced deployment judge", estimate.Unavailable);
    }

    [Fact]
    public void Records_without_a_content_safety_price_give_no_estimate()
    {
        var prices = Prices();
        prices.ContentSafetyPerThousandRecords = null;

        Assert.Equal("no content safety price", CostEstimate.From(Used(), prices).Unavailable);
    }

    [Fact]
    public void Incomplete_usage_gives_no_estimate_but_keeps_counts()
    {
        var usage = Used();
        usage.MarkIncomplete("model usage missing");

        var estimate = CostEstimate.From(usage, Prices());

        Assert.Null(estimate.Amount);
        Assert.False(estimate.Complete);
        Assert.Equal("usage incomplete", estimate.Unavailable);
        Assert.Equal((1_000_000, 100_000), usage.Tokens[Chat]);
    }

    [Fact]
    public void A_turn_that_used_nothing_costs_nothing() =>
        Assert.Equal(0m, CostEstimate.From(new TurnUsage(), Prices()).Amount);
}
