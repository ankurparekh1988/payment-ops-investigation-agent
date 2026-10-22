using PaymentOps.Tests.Shared;

namespace PaymentOps.ArchitectureTests;

/// <summary>
/// The data ports and their records are plain types: how any adapter stores or serializes them
/// stays in that adapter, so a real one can replace the demo data without touching the domain.
/// </summary>
public sealed class DomainSourceRulesTests
{
    private static readonly string Root = Path.Combine(RepositoryRoot.FullPath, "src", "PaymentOps.Domain");

    [Theory]
    [InlineData("Merchants/IMerchantData.cs")]
    [InlineData("Settlement/ISettlementData.cs")]
    [InlineData("Processors/IProcessorData.cs")]
    [InlineData("ErrorEvents/IErrorEventData.cs")]
    [InlineData("Incidents/IIncidentStore.cs")]
    public void The_data_ports_live_in_the_domain(string path) =>
        Assert.True(File.Exists(Path.Combine(Root, path)), $"{path} is missing.");

    [Fact]
    public void Domain_carries_no_serialization_attributes()
    {
        var violations = Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("System.Text.Json", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Root, f));

        Assert.Empty(violations);
    }
}
