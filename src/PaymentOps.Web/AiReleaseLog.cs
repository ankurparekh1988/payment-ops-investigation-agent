using PaymentOps.Platform.AiRelease;

namespace PaymentOps.Web;

/// <summary>
/// Records which AI release this instance runs, as structured fields that become queryable
/// dimensions in Application Insights. Prompt text is never logged.
/// </summary>
internal static partial class AiReleaseLog
{
    public static void Loaded(ILogger logger, AiRelease release)
    {
        ReleaseLoaded(logger, release.SchemaVersion, release.ActiveChatDeployment, release.EmbeddingDeployment, release.JudgeDeployment, release.Prompts.Count);
        foreach (var prompt in release.Prompts.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            PromptLoaded(logger, prompt.Id, prompt.Version, prompt.Sha256);
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "AI release loaded: schema {AiSchemaVersion}, chat {ActiveChatDeployment}, embedding {EmbeddingDeployment}, judge {JudgeDeployment}, {PromptCount} prompts")]
    private static partial void ReleaseLoaded(ILogger logger, int aiSchemaVersion, string activeChatDeployment, string embeddingDeployment, string judgeDeployment, int promptCount);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "AI prompt loaded: {PromptId} {PromptVersion} {PromptSha256}")]
    private static partial void PromptLoaded(ILogger logger, string promptId, string promptVersion, string promptSha256);
}
