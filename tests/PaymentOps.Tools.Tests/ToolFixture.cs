using System.Text.Json.Nodes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Data.Demo;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Tools.Tests;

/// <summary>The real tools over the shipped dataset and demo adapters, with faults when a test asks for them.</summary>
internal sealed class ToolFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public ToolFixture(Dictionary<string, string?>? faults = null, int maxListItems = 20, FakeKnowledgeLookup? knowledge = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(faults ?? []).Build();
        _provider = new ServiceCollection()
            .AddDemoData(configuration, allowFaultInjection: faults is not null)
            .AddSingleton<IKnowledgeLookup>(knowledge ?? new FakeKnowledgeLookup())
            .AddInvestigationTools(options =>
            {
                options.MaxListItems = maxListItems;
                options.RetryBackoff = TimeSpan.Zero;
            })
            .BuildServiceProvider();
    }

    public T Tool<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public static ToolResult.Ok Ok(ToolResult result) => Assert.IsType<ToolResult.Ok>(result);

    public static ToolResult.Failed Failed(ToolResult result, ToolErrorCode code)
    {
        var failed = Assert.IsType<ToolResult.Failed>(result);
        Assert.Equal(code, failed.Code);
        return failed;
    }

    /// <summary>The result exactly as the model would read it.</summary>
    public static JsonNode Json(ToolResult result) => JsonNode.Parse(ToolJson.Serialize(result))!["result"]!;

    public static IReadOnlyList<string> Ids(ToolResult.Ok ok) => [.. ok.Evidence.Select(e => e.Id)];

    public static DerivedEvidence Fact(ToolResult.Ok ok, string id) => Assert.IsType<DerivedEvidence>(Assert.Single(ok.Evidence, e => e.Id == id));

    public void Dispose() => _provider.Dispose();
}

internal sealed class FakeKnowledgeLookup(params RetrievedSection[] sections) : IKnowledgeLookup
{
    public KnowledgeAccess? LastAccess { get; private set; }

    public Exception? Throws { get; init; }

    public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken)
    {
        LastAccess = access;
        return Throws is null
            ? Task.FromResult<IReadOnlyList<RetrievedSection>>([.. sections.Where(s => access.IncludesRestricted || s.Classification == KnowledgeClassification.General)])
            : Task.FromException<IReadOnlyList<RetrievedSection>>(Throws);
    }

    public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
        Task.FromResult<RetrievedSection?>(null);
}
