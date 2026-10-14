using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PaymentOps.Platform.AiRelease;

/// <summary>
/// Rules about how a release may change relative to its base branch:
/// <list type="bullet">
/// <item>a prompt whose body hash changed needs a higher version, and an unchanged one keeps its version;</item>
/// <item>the chat deployment active on the base branch can't be removed in the same change, because
/// the running app keeps using it until the new build is deployed, after the infrastructure apply.</item>
/// </list>
/// Only the fields these rules compare are read, so either manifest may use an older or newer schema;
/// the full validation of the change's own manifest is <see cref="AiReleaseLoader"/>'s job.
/// </summary>
public static class ReleaseChangeCheck
{
    public static IReadOnlyList<string> Compare(byte[] baseManifest, byte[] headManifest)
    {
        ArgumentNullException.ThrowIfNull(baseManifest);
        ArgumentNullException.ThrowIfNull(headManifest);

        var errors = new List<string>();
        var before = Read(baseManifest, "The base branch's manifest", errors);
        var after = Read(headManifest, AiReleaseLoader.ManifestPath, errors);
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

        if (before.ActiveChatDeployment is { } live && !after.ChatDeployments.Contains(live))
        {
            errors.Add($"Chat deployment '{live}' is active on the base branch and removed here. Switch 'active' and remove the old slot in separate changes.");
        }

        return errors;
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<string, (string? Version, string? Sha256)> Prompts,
        string? ActiveChatDeployment,
        IReadOnlySet<string> ChatDeployments);

    private static Snapshot? Read(byte[] manifest, string label, List<string> errors)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StreamReader(new MemoryStream(manifest));
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            errors.Add($"{label} isn't valid YAML (line {ex.Start.Line}): {ex.Message}");
            return null;
        }

        var root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;

        var prompts = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var (key, value) in Mapping(Child(root, "prompts")))
        {
            prompts[Scalar(key) ?? string.Empty] = (Scalar(Child(value, "version")), Scalar(Child(value, "sha256")));
        }

        var chat = Child(Child(root, "models"), "chat");
        var slots = Mapping(Child(chat, "deployments"))
            .ToDictionary(slot => Scalar(slot.Key) ?? string.Empty, slot => Scalar(Child(slot.Value, "deployment")), StringComparer.Ordinal);
        var active = Scalar(Child(chat, "active"));

        return new Snapshot(
            prompts,
            active is not null && slots.TryGetValue(active, out var deployment) ? deployment : null,
            slots.Values.OfType<string>().ToHashSet(StringComparer.Ordinal));
    }

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child : null;

    private static IEnumerable<KeyValuePair<YamlNode, YamlNode>> Mapping(YamlNode? node) =>
        node is YamlMappingNode mapping ? mapping.Children : [];

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;
}
