namespace PaymentOps.Platform.AiRelease;

/// <summary>
/// Loads and verifies an AI release once: the manifest, then each prompt it lists, checking the
/// prompt's own id and version and the hash of its body. Any problem stops the load with every error.
/// </summary>
public static class AiReleaseLoader
{
    public const string ManifestPath = "manifest.yaml";

    private const string Delimiter = "---";

    public static AiRelease Load(IAiReleaseSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var errors = new List<string>();
        var manifest = ReadManifest(source.Read(ManifestPath), errors);
        if (manifest is null)
        {
            throw new AiReleaseException(errors);
        }

        ReleaseManifestValidator.Validate(manifest, errors);

        var prompts = new Dictionary<string, Prompt>(StringComparer.Ordinal);
        foreach (var (id, entry) in manifest.Prompts)
        {
            if (LoadPrompt(source, id, entry, errors) is { } prompt)
            {
                prompts[id] = prompt;
            }
        }

        var listed = manifest.Prompts.Keys.Select(ReleaseManifestValidator.PromptPath).ToHashSet(StringComparer.Ordinal);
        foreach (var unlisted in source.PromptFiles().Where(file => !listed.Contains(file)).Order(StringComparer.Ordinal))
        {
            errors.Add($"{unlisted} isn't listed in the manifest's prompts.");
        }

        if (errors.Count > 0)
        {
            throw new AiReleaseException(errors);
        }

        var models = manifest.Models;
        return new AiRelease(
            manifest.SchemaVersion,
            models.Chat.Deployments[models.Chat.Active].Deployment,
            models.Embedding.Deployment,
            models.Judge.Deployment,
            prompts);
    }

    internal static ReleaseManifest? ReadManifest(byte[]? bytes, List<string> errors)
    {
        if (bytes is null)
        {
            errors.Add($"{ManifestPath} is missing.");
            return null;
        }

        var yaml = AiReleaseText.Decode(bytes, ManifestPath, errors);
        return yaml is null ? null : AiReleaseText.Deserialize<ReleaseManifest>(yaml, ManifestPath, errors);
    }

    private static Prompt? LoadPrompt(IAiReleaseSource source, string id, ManifestPrompt entry, List<string> errors)
    {
        var path = ReleaseManifestValidator.PromptPath(id);
        var bytes = source.Read(path);
        if (bytes is null)
        {
            errors.Add($"{path} is listed in the manifest but missing.");
            return null;
        }

        var text = AiReleaseText.Decode(bytes, path, errors);
        if (text is null || SplitFrontMatter(text, path, errors) is not var (frontMatterYaml, body))
        {
            return null;
        }

        var frontMatter = AiReleaseText.Deserialize<PromptFrontMatter>(frontMatterYaml, path, errors);
        if (frontMatter is null)
        {
            return null;
        }

        var errorCount = errors.Count;
        if (frontMatter.Id != id)
        {
            errors.Add($"{path} declares id '{frontMatter.Id}'; the manifest lists it as '{id}'.");
        }

        if (frontMatter.Version != entry.Version)
        {
            errors.Add($"{path} is version {frontMatter.Version}; the manifest says {entry.Version}.");
        }

        var hash = PromptHasher.Hash(body);
        if (hash != entry.Sha256)
        {
            errors.Add($"{path} body hash is {hash}; the manifest says {entry.Sha256}. Update the manifest, and the version if the body changed.");
        }

        var template = PromptTemplate.Parse(body, path, errors);
        return errors.Count == errorCount && template is not null ? new Prompt(id, entry.Version, hash, template) : null;
    }

    // The first line must be exactly "---"; the next line that is exactly "---" ends the front matter,
    // and everything after that line is the body.
    private static (string FrontMatter, string Body)? SplitFrontMatter(string text, string path, List<string> errors)
    {
        if (!text.StartsWith(Delimiter + "\n", StringComparison.Ordinal))
        {
            errors.Add($"{path} must start with a '{Delimiter}' line opening its front matter.");
            return null;
        }

        var lineStart = Delimiter.Length + 1;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            var line = lineEnd < 0 ? text[lineStart..] : text[lineStart..lineEnd];
            if (line == Delimiter)
            {
                var frontMatter = text[(Delimiter.Length + 1)..lineStart];
                var body = lineEnd < 0 ? string.Empty : text[(lineEnd + 1)..];
                return (frontMatter, body);
            }

            if (lineEnd < 0)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        errors.Add($"{path} has no closing '{Delimiter}' line after its front matter.");
        return null;
    }
}
