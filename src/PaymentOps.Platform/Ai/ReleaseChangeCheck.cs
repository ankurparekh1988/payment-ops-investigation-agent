using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PaymentOps.Platform.Ai;

/// <summary>
/// Rules about how a release may change relative to its base branch:
/// <list type="bullet">
/// <item>a prompt whose body hash changed needs a higher version, and an unchanged one keeps its version;</item>
/// <item>a deployment the base release uses (active chat, embedding, judge) can't be removed or change
/// its model, version, deployment type or content filter, because the running app keeps calling it until
/// the new build is deployed, after the infrastructure apply. Capacity can change.</item>
/// </list>
/// The change's own release has already passed <see cref="AiReleaseLoader"/>. The base manifest may use
/// another schema version, so only the fields these rules compare are read from it.
/// </summary>
public static class ReleaseChangeCheck
{
    public static IReadOnlyList<string> Compare(byte[] baseManifest, AiRelease head)
    {
        ArgumentNullException.ThrowIfNull(baseManifest);
        ArgumentNullException.ThrowIfNull(head);

        var errors = new List<string>();
        var before = ReadBase(baseManifest, errors);
        if (before is null)
        {
            return errors;
        }

        foreach (var (id, (version, sha256)) in before.Prompts)
        {
            if (!head.Prompts.TryGetValue(id, out var current)
                || !PromptVersion.TryParse(version, out var oldVersion)
                || !PromptVersion.TryParse(current.Version, out var newVersion))
            {
                continue;
            }

            if (current.Sha256 != sha256 && !newVersion.IsHigherThan(oldVersion))
            {
                errors.Add($"Prompt '{id}' changed but its version went from {oldVersion} to {newVersion}; it must increase.");
            }
            else if (current.Sha256 == sha256 && newVersion != oldVersion)
            {
                errors.Add($"Prompt '{id}' is unchanged but its version went from {oldVersion} to {newVersion}.");
            }
        }

        foreach (var used in before.InUseDeployments.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            if (!head.Deployments.TryGetValue(used.Name, out var current))
            {
                errors.Add($"Deployment '{used.Name}' is in use on the base branch and removed here. Stop using it in one change and remove it in a later one.");
                continue;
            }

            (string Field, string? Was, string? Now)[] fields =
            [
                ("model", used.Model, current.Model),
                ("version", used.Version, current.Version),
                ("deploymentType", used.DeploymentType, current.DeploymentType),
                ("contentFilterPolicy", used.ContentFilterPolicy, current.ContentFilterPolicy),
            ];

            // A field the base doesn't declare (an older schema) can't be compared.
            foreach (var (field, was, now) in fields.Where(f => f.Was is not null && f.Was != f.Now))
            {
                errors.Add($"Deployment '{used.Name}' is in use on the base branch and its {field} changes here from '{was}' to '{now ?? "none"}'. "
                    + "Put the change under a new deployment name instead, so the running app keeps what it was built for.");
            }
        }

        return errors;
    }

    private sealed record BaseRelease(
        IReadOnlyDictionary<string, (string? Version, string? Sha256)> Prompts,
        IReadOnlyList<BaseDeployment> InUseDeployments);

    private sealed record BaseDeployment(string Name, string? Model, string? Version, string? DeploymentType, string? ContentFilterPolicy);

    private static BaseRelease? ReadBase(byte[] manifest, List<string> errors)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StreamReader(new MemoryStream(manifest));
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            errors.Add($"The base branch's manifest isn't valid YAML (line {ex.Start.Line}): {ex.Message}");
            return null;
        }

        var root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;

        var prompts = Mapping(Child(root, "prompts")).ToDictionary(
            prompt => Scalar(prompt.Key) ?? string.Empty,
            prompt => (Scalar(Child(prompt.Value, "version")), Scalar(Child(prompt.Value, "sha256"))),
            StringComparer.Ordinal);

        var models = Child(root, "models");
        var chat = Child(models, "chat");
        var activeSlot = Child(Child(chat, "deployments"), Scalar(Child(chat, "active")) ?? string.Empty);
        YamlNode?[] inUse = [activeSlot, Child(models, "embedding"), Child(models, "judge")];

        return new BaseRelease(prompts, inUse.Select(ReadDeployment).OfType<BaseDeployment>().ToList());
    }

    private static BaseDeployment? ReadDeployment(YamlNode? node) =>
        Scalar(Child(node, "deployment")) is { } name
            ? new BaseDeployment(
                name,
                Scalar(Child(node, "model")),
                Scalar(Child(node, "version")),
                Scalar(Child(node, "deploymentType")),
                Scalar(Child(node, "contentFilterPolicy")))
            : null;

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child : null;

    private static IEnumerable<KeyValuePair<YamlNode, YamlNode>> Mapping(YamlNode? node) =>
        node is YamlMappingNode mapping ? mapping.Children : [];

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;
}
