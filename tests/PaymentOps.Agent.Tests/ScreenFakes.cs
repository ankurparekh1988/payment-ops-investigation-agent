using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Data.Demo;
using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.ErrorEvents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Tests;

/// <summary>The real read tools over the demo data (optionally rewritten) and one knowledge section.</summary>
internal static class DemoTools
{
    public static IReadOnlyDictionary<string, IInvestigationTool> Build(RetrievedSection? section = null, Func<string, string, string>? rewrite = null)
    {
        var dataset = rewrite is null ? DemoDatasetLoader.LoadDefault() : Rewritten(rewrite);
        var provider = new ServiceCollection()
            .AddSingleton<IMerchantData>(new DemoMerchantData(dataset))
            .AddSingleton<ISettlementData>(new DemoSettlementData(dataset))
            .AddSingleton<IProcessorData>(new DemoProcessorData(dataset))
            .AddSingleton<IErrorEventData>(new DemoErrorEventData(dataset))
            .AddSingleton<IInvestigationClock>(new DemoInvestigationClock(dataset.AsOf))
            .AddSingleton<IKnowledgeLookup>(FakeKnowledgeLookup.Returning(section ?? Section()))
            .AddInvestigationTools()
            .BuildServiceProvider();
        return provider.GetServices<IInvestigationTool>().ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    public static RetrievedSection Section(string title = "Missing batch", string path = "Missing batch > Steps", string content = "Check the file.") =>
        new("ka", "runbook-a", title, path, content, "general", null);

    // The fields written outside this system, rewritten wherever they appear.
    private static readonly HashSet<string> ExternallyAuthored = new(StringComparer.Ordinal) { "name", "summary", "processorReference", "reason", "code" };

    private static DemoDataset Rewritten(Func<string, string, string> rewrite)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, "data", "datasets", "default", "dataset.json")))!;
        Rewrite(root, rewrite);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
        return DemoDatasetLoader.Load(stream);
    }

    private static void Rewrite(JsonNode? node, Func<string, string, string> rewrite)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o.ToList())
                {
                    if (ExternallyAuthored.Contains(key) && value is JsonValue v && v.TryGetValue<string>(out var text))
                    {
                        o[key] = rewrite(key, text);
                    }
                    else
                    {
                        Rewrite(value, rewrite);
                    }
                }

                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    Rewrite(item, rewrite);
                }

                break;
        }
    }
}
