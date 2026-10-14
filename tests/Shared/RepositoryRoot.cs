namespace PaymentOps.Tests.Shared;

/// <summary>The repository root, found by walking up from the test binaries to the solution file.</summary>
internal static class RepositoryRoot
{
    private const string SolutionFile = "PaymentOps.slnx";

    private static readonly Lazy<string> Root = new(Find);

    public static string FullPath => Root.Value;

    private static string Find()
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
