using PaymentOps.Platform.AiRelease;

namespace PaymentOps.Platform.Tests;

/// <summary>The release in the repository is valid, and what's embedded in the build is the same release.</summary>
public sealed class ShippedReleaseTests
{
    [Fact]
    public void The_embedded_release_loads()
    {
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());

        Assert.Equal(1, release.SchemaVersion);
        Assert.False(string.IsNullOrEmpty(release.ActiveChatDeployment));
    }

    [Fact]
    public void The_embedded_files_are_the_repository_files()
    {
        var embedded = new EmbeddedAiReleaseSource();
        var repository = new DirectoryAiReleaseSource(Path.Combine(RepositoryRoot(), "ai"));

        Assert.Equal(repository.Read(AiReleaseLoader.ManifestPath), embedded.Read(AiReleaseLoader.ManifestPath));
        Assert.Equal(repository.PromptFiles().Order(StringComparer.Ordinal), embedded.PromptFiles().Order(StringComparer.Ordinal));
        foreach (var prompt in repository.PromptFiles())
        {
            Assert.Equal(repository.Read(prompt), embedded.Read(prompt));
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PaymentOps.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
