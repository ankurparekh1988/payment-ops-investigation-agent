using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Tools.ErrorEvents;
using PaymentOps.Tools.Knowledge;
using PaymentOps.Tools.Merchants;
using PaymentOps.Tools.Processors;
using PaymentOps.Tools.Settlement;

namespace PaymentOps.Tools;

public static class ToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the read-only tools, each also as <see cref="IInvestigationTool"/>. Needs the data ports,
    /// an <see cref="Domain.Investigation.IInvestigationClock"/> and an <see cref="IKnowledgeLookup"/>.
    /// </summary>
    public static IServiceCollection AddInvestigationTools(this IServiceCollection services, Action<ToolOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<ToolOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        Add<MerchantProfileTool>(services);
        Add<SettlementBatchesTool>(services);
        Add<ProcessorStatusTool>(services);
        Add<ErrorEventsTool>(services);
        Add<KnowledgeSearchTool>(services);
        return services;
    }

    private static void Add<T>(IServiceCollection services)
        where T : class, IInvestigationTool
    {
        services.AddSingleton<T>();
        services.AddSingleton<IInvestigationTool>(sp => sp.GetRequiredService<T>());
    }
}
