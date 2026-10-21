using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Data.Demo;
using PaymentOps.Data.Demo.Faults;
using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.ContractTests;

public sealed class RegistrationTests
{
    [Fact]
    public void All_four_ports_resolve_to_the_demo_adapters()
    {
        using var provider = Provider([]);

        Assert.IsType<DemoMerchantData>(provider.GetRequiredService<IMerchantData>());
        Assert.IsType<DemoSettlementData>(provider.GetRequiredService<ISettlementData>());
        Assert.IsType<DemoProcessorData>(provider.GetRequiredService<IProcessorData>());
        Assert.IsType<DemoErrorEventData>(provider.GetRequiredService<IErrorEventData>());
    }

    // Investigations reason from the dataset's moment; the rest of the app keeps real time.
    [Fact]
    public void The_investigation_clock_is_the_datasets_as_of_time()
    {
        using var provider = Provider([]);
        var clock = provider.GetRequiredService<IInvestigationClock>();

        Assert.Equal(new DateTimeOffset(2026, 11, 11, 6, 0, 0, TimeSpan.Zero), clock.Now);
        Assert.Equal(new DateOnly(2026, 11, 11), clock.Today);
        Assert.Null(provider.GetService<TimeProvider>());
    }

    [Theory]
    [InlineData("DemoData:Faults:Settlement:Mode", "Unavailable")]
    [InlineData("DemoData:Faults:Merchants:Mode", "Hang")]
    public void A_configured_fault_without_the_opt_in_fails_registration(string key, string value)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Provider(new() { [key] = value }));

        Assert.Contains(key, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fault_mode_of_none_needs_no_opt_in()
    {
        using var provider = Provider(new() { ["DemoData:Faults:Settlement:Mode"] = "None" });

        Assert.IsType<DemoSettlementData>(provider.GetRequiredService<ISettlementData>());
    }

    [Fact]
    public void A_configured_fault_with_the_opt_in_decorates_only_that_port()
    {
        using var provider = Provider(new() { ["DemoData:Faults:Settlement:Mode"] = "Empty" }, allowFaultInjection: true);

        Assert.IsType<FaultInjectingSettlementData>(provider.GetRequiredService<ISettlementData>());
        Assert.IsType<DemoMerchantData>(provider.GetRequiredService<IMerchantData>());
    }

    [Theory]
    [InlineData("DemoData:Faults:Payments:Mode", "Empty")]
    [InlineData("DemoData:Faults:Settlement:Mode", "Sometimes")]
    [InlineData("DemoData:Faults:Settlement:Delay", "soon")]
    [InlineData("DemoData:Faults:Settlement:Mode", "3")]
    [InlineData("DemoData:Faults:Settlement:Mode", "Delay, Hang")]
    [InlineData("DemoData:Faults:Settlement:Mode", "empty")]
    [InlineData("DemoData:Faults:Settlement:Delay", "2")]
    [InlineData("DemoData:Faults:Settlement:Delay", "30.00:00:00")]
    [InlineData("DemoData:Faults:Settlement", "Unavailable")]
    public void A_fault_setting_that_isnt_understood_fails_registration(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Provider(new() { [key] = value }, allowFaultInjection: true));

    private static ServiceProvider Provider(Dictionary<string, string?> settings, bool allowFaultInjection = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddDemoData(configuration, allowFaultInjection).BuildServiceProvider();
    }
}
