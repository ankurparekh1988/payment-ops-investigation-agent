using System.Text;

using PaymentOps.Platform.AiRelease;

namespace PaymentOps.Platform.Tests;

/// <summary>An AI release held in memory, so each test states exactly the files it needs.</summary>
internal sealed class TestRelease : IAiReleaseSource
{
    public const string Models = """
        models:
          chat:
            active: blue
            contentFilterPolicy: strict
            deployments:
              blue:
                deployment: chat-blue
                model: gpt-test
                version: "2026-01-01"
                deploymentType: GlobalStandard
                capacityK: 10
          embedding:
            deployment: embed-v1
            model: embed-test
            version: "1"
            deploymentType: GlobalStandard
            capacityK: 10
          judge:
            deployment: judge
            model: gpt-test
            version: "2026-01-01"
            deploymentType: GlobalStandard
            capacityK: 10
            contentFilterPolicy: default
        """;

    public const string Body = "Answer using only the evidence for {{merchantId}}.\n";

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public static string PromptFile(string id = "grounded-answer", string version = "0.1.0", string body = Body) =>
        $"---\nid: {id}\nversion: {version}\n---\n{body}";

    public static string Manifest(string prompts = "prompts: {}", string models = Models, string schema = "schemaVersion: 1") =>
        $"{schema}\n{models}\n{prompts}\n";

    public static string PromptEntry(string id = "grounded-answer", string version = "0.1.0", string body = Body) =>
        $"prompts:\n  {id}:\n    version: {version}\n    sha256: {PromptHasher.Hash(body)}";

    /// <summary>A valid release with one prompt.</summary>
    public static TestRelease WithOnePrompt() =>
        new TestRelease()
            .With("manifest.yaml", Manifest(PromptEntry()))
            .With("prompts/grounded-answer.md", PromptFile());

    public TestRelease With(string path, string text) => With(path, Encoding.UTF8.GetBytes(text));

    public TestRelease With(string path, byte[] bytes)
    {
        _files[path] = bytes;
        return this;
    }

    public byte[]? Read(string path) => _files.GetValueOrDefault(path);

    public IReadOnlyCollection<string> PromptFiles() =>
        _files.Keys.Where(path => path.StartsWith("prompts/", StringComparison.Ordinal)).ToList();

    public IReadOnlyList<string> LoadErrors()
    {
        var ex = Assert.Throws<AiReleaseException>(() => AiReleaseLoader.Load(this));
        return ex.Errors;
    }
}
