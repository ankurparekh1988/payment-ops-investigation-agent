namespace PaymentOps.Platform.Ai;

/// <summary>A prompt from the release, verified against the manifest's version and hash.</summary>
public sealed record Prompt(string Id, string Version, string Sha256, PromptTemplate Template);

/// <summary>
/// The validated AI release built into this artifact: which model deployments the app uses and the
/// prompts it may send. Immutable once loaded.
/// </summary>
/// <param name="Deployments">Every deployment the manifest declares, including inactive chat slots.</param>
public sealed record AiRelease(
    string ActiveChatDeployment,
    string EmbeddingDeployment,
    string JudgeDeployment,
    IReadOnlySet<string> Deployments,
    IReadOnlyDictionary<string, Prompt> Prompts);
