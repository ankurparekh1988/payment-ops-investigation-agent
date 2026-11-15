using System.Text.Json.Nodes;

using PaymentOps.Tests.Shared;

namespace PaymentOps.Platform.Tests.Infrastructure;

/// <summary>The operations workbook shows what the design asks of it, read from the definition Terraform deploys.</summary>
public sealed class WorkbookTests
{
    private static readonly IReadOnlyList<string> Queries = [.. JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot.FullPath, "infra", "modules", "monitoring", "workbook.json")))!["items"]!.AsArray()
        .Select(i => (string?)i!["content"]?["query"])
        .OfType<string>()];

    [Fact]
    public void The_quality_section_shows_tool_calls_by_outcome() =>
        Assert.Contains(Queries, q => q.Contains("startswith 'tool.'", StringComparison.Ordinal) && q.Contains("paymentops.tool.outcome", StringComparison.Ordinal));
}
