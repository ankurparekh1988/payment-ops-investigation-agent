using System.Text.RegularExpressions;

namespace PaymentOps.Platform.Ai;

/// <summary>Every rule about the manifest's content, in one place.</summary>
internal static partial class ReleaseManifestValidator
{
    // Logical names the Foundry Terraform module maps to policies: ours, and Microsoft's default.
    private static readonly string[] ContentFilterPolicies = ["strict", "default"];

    public static void Validate(ReleaseManifest manifest, List<string> errors)
    {
        if (manifest.SchemaVersion != AiReleaseLoader.SupportedSchemaVersion)
        {
            errors.Add($"schemaVersion is {manifest.SchemaVersion}; this build reads version {AiReleaseLoader.SupportedSchemaVersion}.");
        }

        var chat = manifest.Models.Chat;
        if (!chat.Deployments.ContainsKey(chat.Active))
        {
            errors.Add($"models.chat.active is '{chat.Active}', which isn't one of the chat deployments.");
        }

        var deployments = AllDeployments(manifest);
        foreach (var (path, deployment) in deployments)
        {
            CheckDeployment(path, deployment, errors);
            if (deployment is FilteredModelDeployment filtered)
            {
                CheckFilter(path, filtered.ContentFilterPolicy, errors);
            }
        }

        foreach (var duplicate in deployments.GroupBy(d => d.Deployment.Deployment, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"Deployment name '{duplicate.Key}' is used more than once.");
        }

        foreach (var (id, prompt) in manifest.Prompts)
        {
            if (!PromptId().IsMatch(id))
            {
                errors.Add($"Prompt id '{id}' must be lowercase words joined by hyphens.");
            }

            if (!PromptVersion.TryParse(prompt.Version, out _))
            {
                errors.Add($"prompts.{id}.version '{prompt.Version}' isn't MAJOR.MINOR.PATCH.");
            }

            if (!Sha256().IsMatch(prompt.Sha256))
            {
                errors.Add($"prompts.{id}.sha256 must be 'sha256:' followed by 64 lowercase hex digits.");
            }
        }
    }

    public static string PromptPath(string id) => $"prompts/{id}.md";

    /// <summary>Every model deployment the manifest declares, with its path for error messages.</summary>
    public static IReadOnlyList<(string Path, ModelDeployment Deployment)> AllDeployments(ReleaseManifest manifest) =>
        manifest.Models.Chat.Deployments.Select(d => ($"models.chat.deployments.{d.Key}", (ModelDeployment)d.Value))
            .Append(("models.embedding", manifest.Models.Embedding))
            .Append(("models.judge", manifest.Models.Judge))
            .ToList();

    private static void CheckFilter(string path, string policy, List<string> errors)
    {
        if (!ContentFilterPolicies.Contains(policy, StringComparer.Ordinal))
        {
            errors.Add($"{path}.contentFilterPolicy must be one of: {string.Join(", ", ContentFilterPolicies)}.");
        }
    }

    private static void CheckDeployment(string path, ModelDeployment deployment, List<string> errors)
    {
        foreach (var (field, value) in new[]
                 {
                     ("deployment", deployment.Deployment), ("model", deployment.Model),
                     ("version", deployment.Version), ("deploymentType", deployment.DeploymentType),
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{path}.{field} is empty.");
            }
        }

        if (deployment.CapacityK <= 0)
        {
            errors.Add($"{path}.capacityK must be positive.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PromptId();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256();
}
