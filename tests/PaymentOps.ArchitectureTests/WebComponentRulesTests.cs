using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

namespace PaymentOps.ArchitectureTests;

/// <summary>
/// A source-level rule: UI components reach the investigation only through its contract. The Web
/// project still references every module as the composition root; this checks the component files.
/// </summary>
public sealed partial class WebComponentRulesTests
{
    // Everything else under PaymentOps.* (Knowledge, Tools, Data.Demo, Agent internals, Platform) is off limits.
    private static readonly string[] Allowed = ["PaymentOps.Agent.Contracts", "PaymentOps.Web", "PaymentOps.Domain.Security"];

    [Fact]
    public void Components_use_only_the_investigation_contract()
    {
        var root = Path.Combine(RepositoryRoot.FullPath, "src", "PaymentOps.Web", "Components");
        var violations = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => References().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetRelativePath(root, f)}: {m.Value}"))
            .Where(reference => !Allowed.Any(ns => IsWithin(reference[(reference.IndexOf(": ", StringComparison.Ordinal) + 2)..], ns)))
            .ToList();

        Assert.Empty(violations);
    }

    // Tickets are created only by the approval service; no component can reach the store, even by name.
    [Fact]
    public void Components_never_reach_the_incident_store()
    {
        var root = Path.Combine(RepositoryRoot.FullPath, "src", "PaymentOps.Web", "Components");
        var violations = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("IIncidentStore", StringComparison.Ordinal) || File.ReadAllText(f).Contains("Domain.Incidents", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f));

        Assert.Empty(violations);
    }

    private static bool IsWithin(string reference, string ns) =>
        reference == ns || reference.StartsWith(ns + ".", StringComparison.Ordinal);

    // using directives and fully qualified PaymentOps.* references in C# and Razor.
    [GeneratedRegex(@"\bPaymentOps(?:\.[A-Z][A-Za-z]*)+", RegexOptions.CultureInvariant)]
    private static partial Regex References();
}
