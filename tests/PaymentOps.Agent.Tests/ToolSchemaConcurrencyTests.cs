using System.Reflection;
using System.Runtime.Loader;

using PaymentOps.Tools;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// Tool schemas are built lazily from shared serializer options, so the first builds can run on several investigations
/// at once. Each attempt loads its own copy of the agent and tools, because the race only exists before anything is cached.
/// </summary>
public sealed class ToolSchemaConcurrencyTests
{
    private const int Attempts = 20;

    private static readonly string[] RequestTypes =
    [
        "PaymentOps.Tools.Merchants.MerchantProfileRequest",
        "PaymentOps.Tools.Settlement.SettlementBatchesRequest",
        "PaymentOps.Tools.Processors.ProcessorStatusRequest",
        "PaymentOps.Tools.ErrorEvents.ErrorEventsRequest",
        "PaymentOps.Tools.Knowledge.KnowledgeSearchRequest",
    ];

    [Fact]
    public async Task The_first_schemas_can_be_built_concurrently()
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var context = new FreshContext();
            try
            {
                var tools = context.LoadFromAssemblyName(typeof(ToolJson).Assembly.GetName());
                var schema = context.LoadFromAssemblyName(typeof(AgentFixture).Assembly.GetReferencedAssemblies().Single(a => a.Name == "PaymentOps.Agent"))
                    .GetType("PaymentOps.Agent.Investigating.ToolGuard", throwOnError: true)!
                    .GetMethod("Schema", BindingFlags.Public | BindingFlags.Static)!;
                using var start = new Barrier(RequestTypes.Length);

                var builds = RequestTypes.Select(name => tools.GetType(name, throwOnError: true)!)
                    .Select(type => Task.Factory.StartNew(
                        () =>
                        {
                            start.SignalAndWait();
                            schema.Invoke(null, [type]);
                        },
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default))
                    .ToArray();

                await Task.WhenAll(builds);
            }
            finally
            {
                context.Unload();
            }
        }
    }

    // Only the agent and tools are loaded again; the framework and Microsoft.Extensions.AI are shared, as in the app.
    private sealed class FreshContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) =>
            name.Name is "PaymentOps.Agent" or "PaymentOps.Tools"
                ? LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, $"{name.Name}.dll"))
                : null;
    }
}
