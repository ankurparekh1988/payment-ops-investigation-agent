using PaymentOps.Platform.Text;

namespace PaymentOps.Evaluation.Prompts;

/// <summary>
/// A prompt that lives with the evaluation code rather than in the AI release, versioned in its own front matter
/// (<c>id</c>, <c>version</c>) so every report can name the exact prompt it used. Read as strictly as release prompts.
/// </summary>
internal sealed record EvaluationPrompt(string Id, string Version, string Body)
{
    public static EvaluationPrompt Load(string resource)
    {
        using var stream = typeof(EvaluationPrompt).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The prompt resource '{resource}' isn't embedded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        var errors = new List<string>();
        var text = SourceText.Decode(bytes.ToArray(), resource, errors);
        var parts = text is null ? null : SourceText.SplitFrontMatter(text, resource, errors);
        var header = parts is { } split ? SourceText.Deserialize<FrontMatter>(split.FrontMatter, resource, errors) : null;
        return errors.Count == 0 && header is not null && parts is { } found
            ? new EvaluationPrompt(header.Id, header.Version, found.Body.Trim())
            : throw new InvalidOperationException(string.Join(' ', errors));
    }

    private sealed class FrontMatter
    {
        public required string Id { get; init; }

        public required string Version { get; init; }
    }
}
