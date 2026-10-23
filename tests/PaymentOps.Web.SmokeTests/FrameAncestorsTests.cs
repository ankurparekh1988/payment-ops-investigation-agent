namespace PaymentOps.Web.SmokeTests;

public sealed class FrameAncestorsTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    // The page can approve tickets, so no other site may frame it and trick a click.
    [Fact]
    public async Task The_home_page_forbids_framing()
    {
        using var client = factory.CreateReaderClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("frame-ancestors 'none'", string.Join(";", response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }
}
