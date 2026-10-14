namespace PaymentOps.Platform.AiRelease;

/// <summary>A prompt from the release, verified against the manifest's version and hash.</summary>
public sealed class Prompt(string id, string version, string sha256, PromptTemplate template)
{
    public string Id { get; } = id;

    public string Version { get; } = version;

    public string Sha256 { get; } = sha256;

    public PromptTemplate Template { get; } = template;
}

/// <summary>
/// The validated AI release built into this artifact: which model deployments the app uses and the
/// prompts it may send. Immutable once loaded.
/// </summary>
public sealed class AiRelease(
    int schemaVersion,
    string activeChatDeployment,
    string embeddingDeployment,
    string judgeDeployment,
    IReadOnlyDictionary<string, Prompt> prompts)
{
    public int SchemaVersion { get; } = schemaVersion;

    public string ActiveChatDeployment { get; } = activeChatDeployment;

    public string EmbeddingDeployment { get; } = embeddingDeployment;

    public string JudgeDeployment { get; } = judgeDeployment;

    public IReadOnlyDictionary<string, Prompt> Prompts { get; } = prompts;
}
