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
/// <param name="KnowledgeIndex">The search index the knowledge corpus is published to.</param>
/// <param name="RetrievalMode">How the knowledge index is queried.</param>
/// <param name="SemanticFloor">Minimum reranker score a semantic hit needs to count as evidence; null for none.</param>
/// <param name="Deployments">Every deployment the manifest declares, by name, including inactive chat slots.</param>
public sealed record AiRelease(
    string ActiveChatDeployment,
    string EmbeddingDeployment,
    string JudgeDeployment,
    string KnowledgeIndex,
    RetrievalMode RetrievalMode,
    double? SemanticFloor,
    IReadOnlyDictionary<string, DeploymentIdentity> Deployments,
    IReadOnlyDictionary<string, Prompt> Prompts);
