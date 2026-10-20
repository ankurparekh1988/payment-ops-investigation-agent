using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Web.SmokeTests;

public sealed class DemoDataWiringTests
{
    [Fact]
    public void The_app_resolves_every_data_port()
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        var services = factory.Services;

        Assert.NotNull(services.GetRequiredService<IMerchantData>());
        Assert.NotNull(services.GetRequiredService<ISettlementData>());
        Assert.NotNull(services.GetRequiredService<IProcessorData>());
        Assert.NotNull(services.GetRequiredService<IErrorEventData>());
    }

    // An app setting alone can't turn fault injection on, so it can't be switched on in a deployment.
    [Fact]
    public void A_fault_setting_in_configuration_stops_startup()
    {
        using var factory = new PaymentOpsWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("DemoData:Faults:Settlement:Mode", "Unavailable"));

        Assert.ThrowsAny<Exception>(() => configured.CreateClient());
    }
}
