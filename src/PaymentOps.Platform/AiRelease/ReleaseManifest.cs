namespace PaymentOps.Platform.AiRelease;

// The YAML shape of ai/manifest.yaml. Every member is required unless nullable, and unknown keys
// are rejected, so a typo fails loading instead of silently taking a default.

internal sealed class ReleaseManifest
{
    public required int SchemaVersion { get; init; }

    public required ManifestModels Models { get; init; }

    public required Dictionary<string, ManifestPrompt> Prompts { get; init; }
}

internal sealed class ManifestModels
{
    public required ChatModels Chat { get; init; }

    public required ModelDeployment Embedding { get; init; }

    public required ModelDeployment Judge { get; init; }
}

internal sealed class ChatModels
{
    public required string Active { get; init; }

    public required string ContentFilterPolicy { get; init; }

    public required Dictionary<string, ModelDeployment> Deployments { get; init; }
}

internal sealed class ModelDeployment
{
    public required string Deployment { get; init; }

    public required string Model { get; init; }

    public required string Version { get; init; }

    public required string DeploymentType { get; init; }

    public required int CapacityK { get; init; }

    public string? ContentFilterPolicy { get; init; }
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
