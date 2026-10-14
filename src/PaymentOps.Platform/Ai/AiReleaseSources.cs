using System.Reflection;

namespace PaymentOps.Platform.Ai;

/// <summary>Where the release files come from, addressed by paths relative to <c>ai/</c>.</summary>
public interface IAiReleaseSource
{
    /// <summary>The file's bytes, or null if it doesn't exist.</summary>
    byte[]? Read(string path);

    /// <summary>Every prompt file present, as <c>prompts/&lt;name&gt;.md</c>.</summary>
    IReadOnlyCollection<string> PromptFiles();
}

/// <summary>The release embedded in this assembly at build time: what the app runs.</summary>
public sealed class EmbeddedAiReleaseSource : IAiReleaseSource
{
    private const string Root = "ai/";

    private static readonly Assembly Assembly = typeof(EmbeddedAiReleaseSource).Assembly;

    public byte[]? Read(string path)
    {
        using var stream = Assembly.GetManifestResourceStream(Root + path);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public IReadOnlyCollection<string> PromptFiles() =>
        Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Root + "prompts/", StringComparison.Ordinal))
            .Select(name => name[Root.Length..])
            .ToList();
}

/// <summary>The release as files in a working tree, for checks that run before anything is built.</summary>
public sealed class DirectoryAiReleaseSource(string aiDirectory) : IAiReleaseSource
{
    public byte[]? Read(string path)
    {
        var file = Path.Combine(aiDirectory, path);
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    public IReadOnlyCollection<string> PromptFiles()
    {
        var prompts = Path.Combine(aiDirectory, "prompts");
        return Directory.Exists(prompts)
            ? Directory.EnumerateFiles(prompts, "*.md").Select(file => "prompts/" + Path.GetFileName(file)).ToList()
            : [];
    }
}
