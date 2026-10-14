namespace PaymentOps.Platform.Ai;

/// <summary>
/// Loads and verifies an AI release once: the manifest, then each prompt it lists, checking the
/// prompt's own id and version and the hash of its body. Any problem stops the load with every error.
/// </summary>
public static class AiReleaseLoader
{
    public const string ManifestPath = "manifest.yaml";

    /// <summary>The manifest format this build reads.</summary>
    public const int SupportedSchemaVersion = 1;

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
            models.Chat.Deployments[models.Chat.Active].Deployment,
            models.Embedding.Deployment,
            models.Judge.Deployment,
            ReleaseManifestValidator.AllDeployments(manifest).Select(d => d.Deployment.Deployment).ToHashSet(StringComparer.Ordinal),
            prompts);
    }

    private static ReleaseManifest? ReadManifest(byte[]? bytes, List<string> errors)
    {
        if (bytes is null)
        {
            errors.Add($"{ManifestPath} is missing.");
            return null;
        }

        var yaml = AiReleaseText.Decode(bytes, ManifestPath, errors);
        return yaml is null ? null : AiReleaseText.Deserialize<ReleaseManifest>(yaml, ManifestPath, errors);
    }

    // A prompt that adds errors is still returned; Load throws before any of them are used.
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
        return template is null ? null : new Prompt(id, entry.Version, hash, template);
    }

    // The first line must be exactly "---"; the next line that is exactly "---" ends the front matter,
    // and everything after that line is the body. Line endings are already LF.
    private static (string FrontMatter, string Body)? SplitFrontMatter(string text, string path, List<string> errors)
    {
        const string Opening = Delimiter + "\n";
        const string Closing = "\n" + Delimiter;

        if (!text.StartsWith(Opening, StringComparison.Ordinal))
        {
            errors.Add($"{path} must start with a '{Delimiter}' line opening its front matter.");
            return null;
        }

        // Searching from the opening line's own newline also finds an empty front matter.
        var close = text.IndexOf(Closing + "\n", Opening.Length - 1, StringComparison.Ordinal);
        if (close >= 0)
        {
            return (text[Opening.Length..(close + 1)], text[(close + Closing.Length + 1)..]);
        }

        if (text.Length > Opening.Length && text.EndsWith(Closing, StringComparison.Ordinal))
        {
            return (text[Opening.Length..(text.Length - Delimiter.Length)], string.Empty);
        }

        errors.Add($"{path} has no closing '{Delimiter}' line after its front matter.");
        return null;
    }
}
