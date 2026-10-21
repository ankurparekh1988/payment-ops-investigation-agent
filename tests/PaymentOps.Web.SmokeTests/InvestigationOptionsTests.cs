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
}
