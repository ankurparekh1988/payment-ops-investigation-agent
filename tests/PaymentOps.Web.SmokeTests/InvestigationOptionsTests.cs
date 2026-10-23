using Microsoft.AspNetCore.Hosting;

namespace PaymentOps.Web.SmokeTests;

public sealed class InvestigationOptionsTests
{
    [Theory]
    [InlineData("PaymentOps:Investigation:MaxToolCalls", "0")]
    [InlineData("PaymentOps:Investigation:ToolTimeout", "00:00:00")]
    [InlineData("PaymentOps:Investigation:TurnTimeout", "00:06:00")]
    [InlineData("PaymentOps:Investigation:MaxCorrections", "-1")]
    [InlineData("PaymentOps:Tools:MaxListItems", "0")]
    [InlineData("PaymentOps:Tools:MaxQueryLength", "0")]
    public void Invalid_investigation_limits_stop_the_host(string key, string value)
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        Assert.ThrowsAny<Exception>(() => configured.CreateClient());
    }

    // The history budget is checked where the agent is registered, so these need the endpoints that register it.
    [Theory]
    [InlineData("PaymentOps:Conversation:InputBudgetTokens", "24000")]
    [InlineData("PaymentOps:Investigation:MaxToolCalls", "12")]
    public void A_budget_that_leaves_no_history_stops_the_host(string key, string value)
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => Configured(b).UseSetting(key, value));

        Assert.ThrowsAny<Exception>(() => configured.CreateClient());
    }

    [Fact]
    public void The_default_budget_starts_the_host()
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => Configured(b));

        using var client = configured.CreateClient();
    }

    private static IWebHostBuilder Configured(IWebHostBuilder builder) => builder
        .UseSetting("PaymentOps:Search:Endpoint", "https://search.example")
        .UseSetting("PaymentOps:Foundry:Endpoint", "https://foundry.example/")
        .UseSetting("PaymentOps:Foundry:TokenScope", "https://cognitiveservices.azure.com/.default");
}
