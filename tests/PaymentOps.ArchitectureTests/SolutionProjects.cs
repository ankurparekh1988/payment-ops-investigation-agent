using System.Xml.Linq;

using PaymentOps.Tests.Shared;

namespace PaymentOps.ArchitectureTests;

/// <summary>
/// Reads the dependency graph from the project files rather than from compiled assemblies,
/// so a rule is enforced as soon as a reference is declared, even before any code uses it.
/// </summary>
internal static class SolutionProjects
{
    /// <summary>All production projects under <c>src/</c>, keyed by project name.</summary>
    public static IReadOnlyDictionary<string, ProjectInfo> Source() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.FullPath, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Load)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

    private static ProjectInfo Load(string path)
    {
        var xml = XDocument.Load(path);
        // Project files use Windows separators; normalise them so the names resolve on Linux too.
        var projectReferences = xml.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
            .ToHashSet(StringComparer.Ordinal);
        var packageReferences = xml.Descendants("PackageReference")
            .Select(e => (string)e.Attribute("Include")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sdk = (string?)xml.Root?.Attribute("Sdk") ?? string.Empty;

        return new ProjectInfo(Path.GetFileNameWithoutExtension(path), sdk, projectReferences, packageReferences);
    }
}

internal sealed record ProjectInfo(
    string Name,
    string Sdk,
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences);
