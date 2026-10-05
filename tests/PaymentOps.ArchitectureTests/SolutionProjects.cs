using System.Xml.Linq;

namespace PaymentOps.ArchitectureTests;

/// <summary>
/// Reads the dependency graph from the project files rather than from compiled assemblies,
/// so a rule is enforced as soon as a reference is declared, even before any code uses it.
/// </summary>
internal static class SolutionProjects
{
    private const string SolutionFile = "PaymentOps.slnx";

    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    public static string RepositoryRoot => Root.Value;

    /// <summary>All production projects under <c>src/</c>, keyed by project name.</summary>
    public static IReadOnlyDictionary<string, ProjectInfo> Source() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Load)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

    private static ProjectInfo Load(string path)
    {
        var xml = XDocument.Load(path);
        var projectReferences = xml.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension((string)e.Attribute("Include")!))
            .ToHashSet(StringComparer.Ordinal);
        var packageReferences = xml.Descendants("PackageReference")
            .Select(e => (string)e.Attribute("Include")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sdk = (string?)xml.Root?.Attribute("Sdk") ?? string.Empty;

        return new ProjectInfo(Path.GetFileNameWithoutExtension(path), sdk, projectReferences, packageReferences);
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find {SolutionFile} above {AppContext.BaseDirectory}.");
    }
}

internal sealed record ProjectInfo(
    string Name,
    string Sdk,
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences);
