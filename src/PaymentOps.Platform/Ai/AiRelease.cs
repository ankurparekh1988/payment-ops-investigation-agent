namespace PaymentOps.Platform.Ai;

/// <summary>A prompt from the release, verified against the manifest's version and hash.</summary>
public sealed record Prompt(string Id, string Version, string Sha256, PromptTemplate Template);

/// <summary>
/// What calling a deployment name gets you. Capacity is left out: it changes throughput, not behaviour.
/// </summary>
/// <param name="ContentFilterPolicy">Null where no filter is attached (the embedding).</param>
public sealed record DeploymentIdentity(string Model, string Version, string DeploymentType, string? ContentFilterPolicy);

/// <summary>
/// The validated AI release built into this artifact: which model deployments the app uses and the
/// prompts it may send. Immutable once loaded.
/// </summary>
/// <param name="Deployments">Every deployment the manifest declares, by name, including inactive chat slots.</param>
public sealed record AiRelease(
    string ActiveChatDeployment,
    string EmbeddingDeployment,
    string JudgeDeployment,
    IReadOnlyDictionary<string, DeploymentIdentity> Deployments,
    IReadOnlyDictionary<string, Prompt> Prompts);
