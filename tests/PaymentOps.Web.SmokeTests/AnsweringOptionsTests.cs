namespace PaymentOps.Web.SmokeTests;

public sealed class AnsweringOptionsTests
{
    [Theory]
    [InlineData("PaymentOps:Answering:MaxQuestionLength", "0")]
    [InlineData("PaymentOps:Answering:MaxOutputTokens", "0")]
    [InlineData("PaymentOps:Answering:SearchTimeout", "-00:00:01")]
    [InlineData("PaymentOps:Answering:ModelTimeout", "00:06:00")]
    public void Invalid_answering_limits_stop_the_host(string key, string value)
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        Assert.ThrowsAny<Exception>(() => configured.CreateClient());
    }

    [Fact]
    public void The_default_limits_start_the_host()
    {
        using var factory = new PaymentOpsWebApplicationFactory();

        using var client = factory.CreateClient();
    }
}
