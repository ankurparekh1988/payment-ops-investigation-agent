namespace PaymentOps.Platform.Ai;

// The YAML shape of ai/manifest.yaml. Every member is required and non-null, and unknown keys are
// rejected, so a typo or an empty value fails loading instead of silently taking a default. A key is
// only accepted where something reads it: contentFilterPolicy exists on chat deployments and the judge.

internal sealed class ReleaseManifest
{
    public required int SchemaVersion { get; init; }

    public required ManifestModels Models { get; init; }

    public required ManifestRetrieval Retrieval { get; init; }

    public required Dictionary<string, ManifestPrompt> Prompts { get; init; }
}

internal sealed class ManifestModels
{
    public required ChatModels Chat { get; init; }

    public required ModelDeployment Embedding { get; init; }

    public required FilteredModelDeployment Judge { get; init; }
}

internal sealed class ChatModels
{
    public required string Active { get; init; }

    // The filter is per slot, so a filter change can go through blue/green like a model change.
    public required Dictionary<string, FilteredModelDeployment> Deployments { get; init; }
}

internal class ModelDeployment
{
    public required string Deployment { get; init; }

    public required string Model { get; init; }

    public required string Version { get; init; }

    public required string DeploymentType { get; init; }

    public required int CapacityK { get; init; }
}

internal sealed class FilteredModelDeployment : ModelDeployment
{
    public required string ContentFilterPolicy { get; init; }
}

internal sealed class ManifestRetrieval
{
    // A breaking schema change gets a new name, so a live index is never changed incompatibly.
    public required string Index { get; init; }
}

internal sealed class ManifestPrompt
{
    public required string Version { get; init; }

    public required string Sha256 { get; init; }
}

internal sealed class PromptFrontMatter
{
    public required string Id { get; init; }

    public required string Version { get; init; }
}
