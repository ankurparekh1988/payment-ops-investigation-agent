using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Data.Demo.Faults;
using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo;

/// <summary>The port names used in fault settings and in <c>DataSourceUnavailableException.DataSource</c>.</summary>
internal static class DemoDataPorts
{
    public const string Merchants = "Merchants";
    public const string Settlement = "Settlement";
    public const string Processors = "Processors";
    public const string ErrorEvents = "ErrorEvents";

    public static readonly string[] All = [Merchants, Settlement, Processors, ErrorEvents];
}

public static class DemoDataServiceCollectionExtensions
{
    private const string FaultsSection = "DemoData:Faults";
    private static readonly TimeSpan DefaultFaultDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxFaultDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Registers the data ports over the built-in demo dataset, which is loaded and checked now so a bad
    /// dataset stops startup. Fault injection runs only when the host passes <paramref name="allowFaultInjection"/>:
    /// configuration alone can't switch it on, so an app setting can't turn it on in a deployment.
    /// </summary>
    public static IServiceCollection AddDemoData(this IServiceCollection services, IConfiguration configuration, bool allowFaultInjection = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var faults = ReadFaults(configuration.GetSection(FaultsSection), allowFaultInjection);
        var dataset = DemoDatasetLoader.LoadDefault();

        services.AddSingleton<IMerchantData>(Decorated<IMerchantData>(new DemoMerchantData(dataset), faults, DemoDataPorts.Merchants, (port, f) => new FaultInjectingMerchantData(port, f)));
        services.AddSingleton<ISettlementData>(Decorated<ISettlementData>(new DemoSettlementData(dataset), faults, DemoDataPorts.Settlement, (port, f) => new FaultInjectingSettlementData(port, f)));
        services.AddSingleton<IProcessorData>(Decorated<IProcessorData>(new DemoProcessorData(dataset), faults, DemoDataPorts.Processors, (port, f) => new FaultInjectingProcessorData(port, f)));
        services.AddSingleton<IErrorEventData>(Decorated<IErrorEventData>(new DemoErrorEventData(dataset), faults, DemoDataPorts.ErrorEvents, (port, f) => new FaultInjectingErrorEventData(port, f)));
        return services;
    }

    private static T Decorated<T>(T port, Dictionary<string, FaultOptions> faults, string name, Func<T, FaultInjector, T> decorate) =>
        faults.TryGetValue(name, out var options) ? decorate(port, new FaultInjector(options, name)) : port;

    // Strict, like the dataset: a typo in a port, mode or delay fails rather than silently injecting nothing.
    private static Dictionary<string, FaultOptions> ReadFaults(IConfigurationSection section, bool allowFaultInjection)
    {
        var faults = new Dictionary<string, FaultOptions>(StringComparer.Ordinal);
        foreach (var port in section.GetChildren())
        {
            var key = $"{FaultsSection}:{port.Key}";
            if (!DemoDataPorts.All.Contains(port.Key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{key} isn't a data port; use one of {string.Join(", ", DemoDataPorts.All)}.");
            }

            // Some hosts also give a section with children a value, so only a bare value is a mistake.
            if (port.Value is not null && !port.GetChildren().Any())
            {
                throw new InvalidOperationException($"{key} needs a Mode (and optionally a Delay) beneath it, not a value.");
            }

            var unknown = port.GetChildren().Select(c => c.Key).Except(["Mode", "Delay"], StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException($"{key} has unknown settings: {string.Join(", ", unknown)}.");
            }

            // Exact names only: Enum.TryParse would also take "3" or "Delay, Hang" and quietly pick another mode.
            var modeText = port["Mode"] ?? nameof(FaultMode.None);
            if (!Enum.GetNames<FaultMode>().Contains(modeText, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{key}:Mode '{modeText}' isn't one of {string.Join(", ", Enum.GetNames<FaultMode>())}.");
            }

            var mode = Enum.Parse<FaultMode>(modeText);

            var delay = DefaultFaultDelay;
            // hh:mm:ss only: a bare "2" would parse as two days.
            if (port["Delay"] is { } delayText
                && (!TimeSpan.TryParseExact(delayText, @"hh\:mm\:ss\.FFF", CultureInfo.InvariantCulture, out delay) || delay > MaxFaultDelay))
            {
                throw new InvalidOperationException($"{key}:Delay '{delayText}' must be a time span such as 00:00:02, up to {MaxFaultDelay}.");
            }

            if (mode != FaultMode.None && !allowFaultInjection)
            {
                throw new InvalidOperationException(
                    $"{key}:Mode is {mode}, but fault injection is only for test hosts, which enable it in code. Remove the setting.");
            }

            if (mode != FaultMode.None)
            {
                faults[port.Key] = new FaultOptions(mode, delay);
            }
        }

        return faults;
    }
}
