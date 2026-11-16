using Azure.Core;
using Azure.Data.Tables;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Usage;

internal static class UsageServiceCollectionExtensions
{
    public const string TableName = "usage";

    /// <summary>
    /// The pause switch and daily limits, with their counts in the deployment's table. Without a table endpoint only
    /// Development runs, counting in memory; anywhere else that's a deployment mistake, so startup fails.
    /// </summary>
    public static IServiceCollection AddUsageGuard(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(UsageOptions.SectionName);
        services.AddOptions<UsageOptions>()
            .Bind(section)
            .Validate(o => o.DailyLimitPerUser > 0 && o.DailyLimitTotal > 0, $"{UsageOptions.SectionName} needs positive daily limits.")
            .ValidateOnStart();

        var endpoint = section.Get<UsageOptions>()?.TableEndpoint;
        if (endpoint is null && !environment.IsDevelopment())
        {
            throw new InvalidOperationException($"{UsageOptions.SectionName}:TableEndpoint is required outside Development.");
        }

        if (endpoint is null)
        {
            services.AddSingleton<IUsageCounter, InMemoryUsageCounter>();
            services.AddSingleton<IPauseStore, InMemoryPauseStore>();
            return services;
        }

        // The counts and the Admin's pause share one table.
        services.AddSingleton(sp => new TableClient(endpoint, TableName, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IUsageCounter>(sp => new TableUsageCounter(sp.GetRequiredService<TableClient>()));
        services.AddSingleton<IPauseStore>(sp => new TablePauseStore(sp.GetRequiredService<TableClient>(), sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }

    /// <summary>Wraps each investigation in the usage guard; the agent puts it inside its one-at-a-time gate.</summary>
    public static IInvestigationService Guard(IServiceProvider services, IInvestigationService investigations) =>
        ActivatorUtilities.CreateInstance<UsageGuard>(services, investigations);
}
