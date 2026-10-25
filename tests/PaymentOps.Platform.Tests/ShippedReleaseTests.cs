using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Platform.Tests;

/// <summary>The release in the repository is valid, and what's embedded in the build is the same release.</summary>
public sealed class ShippedReleaseTests
{
    [Fact]
    public void The_embedded_release_loads()
    {
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());

        Assert.False(string.IsNullOrEmpty(release.ActiveChatDeployment));
    }

    [Fact]
    public void The_embedded_files_are_the_repository_files()
    {
        var embedded = new EmbeddedAiReleaseSource();
        var repository = new DirectoryAiReleaseSource(Path.Combine(RepositoryRoot.FullPath, "ai"));

        Assert.Equal(repository.Read(AiReleaseLoader.ManifestPath), embedded.Read(AiReleaseLoader.ManifestPath));
        Assert.Equal(repository.PromptFiles().Order(StringComparer.Ordinal), embedded.PromptFiles().Order(StringComparer.Ordinal));
        foreach (var prompt in repository.PromptFiles())
        {
            Assert.Equal(repository.Read(prompt), embedded.Read(prompt));
        }
    }

    [Fact]
    public void The_shipped_release_names_the_knowledge_index()
    {
        Assert.Equal("knowledge-v1", AiReleaseLoader.Load(new EmbeddedAiReleaseSource()).KnowledgeIndex);
    }

    [Fact]
    public void The_release_names_its_evaluation_index()
    {
        Assert.Equal("knowledge-eval-v1", AiReleaseLoader.Load(new EmbeddedAiReleaseSource()).EvaluationIndex);
    }

    [Fact]
    public void The_shipped_release_retrieves_lexically_without_a_floor()
    {
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());

        Assert.Equal(RetrievalMode.Lexical, release.RetrievalMode);
        Assert.Null(release.SemanticFloor);
    }

    [Fact]
    public void The_shipped_release_has_only_the_investigation_prompt()
    {
        Assert.Equal(["investigation-agent"], AiReleaseLoader.Load(new EmbeddedAiReleaseSource()).Prompts.Keys);
    }
}
