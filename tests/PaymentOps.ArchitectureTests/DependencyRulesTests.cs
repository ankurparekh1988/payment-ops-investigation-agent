namespace PaymentOps.ArchitectureTests;

/// <summary>
/// Architecture fitness functions: the module boundaries of the single deployable are
/// enforced by project references, and these tests fail the build when a boundary is crossed.
/// </summary>
public sealed class DependencyRulesTests
{
    private const string Domain = "PaymentOps.Domain";
    private const string Tools = "PaymentOps.Tools";
    private const string DemoAdapters = "PaymentOps.Data.Demo";
    private const string Knowledge = "PaymentOps.Knowledge";
    private const string Web = "PaymentOps.Web";

    private static readonly string[] ForbiddenDomainPackagePrefixes =
        ["Azure.", "Microsoft.Azure.", "Microsoft.Extensions.AI", "Microsoft.Agents.", "Microsoft.AspNetCore.", "OpenAI"];

    private readonly IReadOnlyDictionary<string, ProjectInfo> _projects = SolutionProjects.Source();

    [Fact]
    public void Expected_source_projects_exist()
    {
        string[] expected =
            [Web, "PaymentOps.Agent", Tools, Domain, DemoAdapters, Knowledge, "PaymentOps.Platform"];

        Assert.Equal(expected.Order(StringComparer.Ordinal), _projects.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Domain_has_no_project_references()
    {
        Assert.Empty(_projects[Domain].ProjectReferences);
    }

    [Fact]
    public void Domain_references_no_azure_ai_or_web_packages()
    {
        var forbidden = _projects[Domain].PackageReferences
            .Where(p => ForbiddenDomainPackagePrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(forbidden);
        Assert.Equal("Microsoft.NET.Sdk", _projects[Domain].Sdk);
    }

    [Fact]
    public void Tools_depend_on_ports_not_on_adapters_or_the_host()
    {
        Assert.DoesNotContain(DemoAdapters, _projects[Tools].ProjectReferences);
        Assert.DoesNotContain(Web, _projects[Tools].ProjectReferences);
    }

    [Fact]
    public void Only_the_composition_root_references_demo_adapters()
    {
        var referencing = _projects.Values
            .Where(p => p.ProjectReferences.Contains(DemoAdapters))
            .Select(p => p.Name);

        Assert.Equal([Web], referencing);
    }

    [Fact]
    public void Only_knowledge_uses_the_azure_ai_search_sdk()
    {
        var referencing = _projects.Values
            .Where(p => p.PackageReferences.Contains("Azure.Search.Documents"))
            .Select(p => p.Name);

        Assert.All(referencing, name => Assert.Equal(Knowledge, name));
    }

    [Fact]
    public void Nothing_references_the_web_host()
    {
        var referencing = _projects.Values
            .Where(p => p.ProjectReferences.Contains(Web))
            .Select(p => p.Name);

        Assert.Empty(referencing);
    }
}
