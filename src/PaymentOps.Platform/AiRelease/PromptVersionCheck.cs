using YamlDotNet.RepresentationModel;

namespace PaymentOps.Platform.AiRelease;

/// <summary>
/// Compares a change's manifest with its base branch's: a prompt whose body hash changed must have
/// a higher version, and one whose body didn't change must keep its version. New and removed
/// prompts are allowed.
/// </summary>
public static class PromptVersionCheck
{
    public static IReadOnlyList<string> Compare(byte[] baseManifest, byte[] headManifest)
    {
        ArgumentNullException.ThrowIfNull(baseManifest);
        ArgumentNullException.ThrowIfNull(headManifest);

        var errors = new List<string>();
        if (!DeclaresSchemaVersion(baseManifest))
        {
            // The base predates prompt versioning, so it has no prompts to compare against.
            return errors;
        }

        var before = AiReleaseLoader.ReadManifest(baseManifest, errors);
        var after = AiReleaseLoader.ReadManifest(headManifest, errors);
        if (before is null || after is null)
        {
            return errors;
        }

        foreach (var (id, previous) in before.Prompts)
        {
            if (!after.Prompts.TryGetValue(id, out var current)
                || !PromptVersion.TryParse(previous.Version, out var oldVersion)
                || !PromptVersion.TryParse(current.Version, out var newVersion))
            {
                continue;
            }

            if (current.Sha256 != previous.Sha256 && !newVersion.IsHigherThan(oldVersion))
            {
                errors.Add($"Prompt '{id}' changed but its version went from {oldVersion} to {newVersion}; it must increase.");
            }
            else if (current.Sha256 == previous.Sha256 && newVersion != oldVersion)
            {
                errors.Add($"Prompt '{id}' is unchanged but its version went from {oldVersion} to {newVersion}.");
            }
        }

        return errors;
    }

    private static bool DeclaresSchemaVersion(byte[] manifest)
    {
        var stream = new YamlStream();
        using var reader = new StreamReader(new MemoryStream(manifest));
        stream.Load(reader);
        return stream.Documents.Count > 0
            && stream.Documents[0].RootNode is YamlMappingNode root
            && root.Children.ContainsKey(new YamlScalarNode("schemaVersion"));
    }
}
