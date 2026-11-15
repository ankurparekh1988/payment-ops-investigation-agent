using Microsoft.Extensions.Configuration;

using PaymentOps.Agent.Telemetry;

namespace PaymentOps.Web.SmokeTests;

/// <summary>Prices arrive as App Service settings, whose names can't carry a deployment's hyphen, so they're a list.</summary>
public sealed class CostEstimateOptionsTests
{
    [Fact]
    public void Prices_bind_from_indexed_settings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentOps:CostEstimate:Currency"] = "USD",
            ["PaymentOps:CostEstimate:Deployments:0:Deployment"] = "chat-blue",
            ["PaymentOps:CostEstimate:Deployments:0:InputPerMillion"] = "0.75",
            ["PaymentOps:CostEstimate:Deployments:0:OutputPerMillion"] = "4.5",
            ["PaymentOps:CostEstimate:ContentSafetyPerThousandRecords"] = "0.38",
        }).Build();

        var options = configuration.GetSection(CostEstimateOptions.SectionName).Get<CostEstimateOptions>()!;

        var price = Assert.Single(options.Deployments);
        Assert.Equal(("chat-blue", 0.75m, 4.5m), (price.Deployment, price.InputPerMillion, price.OutputPerMillion));
        Assert.Equal(0.38m, options.ContentSafetyPerThousandRecords);
    }
}
