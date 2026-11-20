using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

public sealed class AccessOptionsTests
{
    [Theory]
    [InlineData("IdleTimeout", "00:00:00")]
    [InlineData("IdleTimeout", "01:01:00")]
    [InlineData("RecentAuthenticationWindow", "00:00:00")]
    [InlineData("RecentAuthenticationWindow", "01:01:00")]
    [InlineData("IdleWarning", "00:15:00")]
    [InlineData("IdleWarning", "00:00:00")]
    [InlineData("SessionLifetime", "00:15:00")]
    [InlineData("SessionLifetime", "00:10:00")]
    public void Options_validation(string key, string value)
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting($"{AccessOptions.SectionName}:{key}", value));

        Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
    }

    [Fact]
    public void Invalid_options_stop_the_host_when_sign_in_is_not_configured()
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => b
            .UseSetting("AzureAd:ClientId", string.Empty)
            .UseSetting($"{AccessOptions.SectionName}:IdleTimeout", "00:00:00"));

        Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
    }

    [Fact]
    public void The_defaults_start_the_host()
    {
        using var factory = new PaymentOpsWebApplicationFactory();

        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<AccessOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(15), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), options.IdleWarning);
        Assert.Equal(TimeSpan.FromMinutes(15), options.RecentAuthenticationWindow);
        Assert.Equal(TimeSpan.FromHours(8), options.SessionLifetime);
    }
}
