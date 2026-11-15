using PaymentOps.Agent.Investigating;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Telemetry;

/// <summary>
/// What a trace names about the release it ran under, from the AI release the app or evaluation loaded: the prompt,
/// the chat deployment and the search index. All are fixed identifiers, never free text.
/// </summary>
internal sealed record TraceContext(string PromptId, string PromptVersion, string ChatDeployment, string KnowledgeIndex)
{
    public static TraceContext From(AiRelease release) => new(
        InvestigationAgentRunner.PromptId,
        release.Prompts[InvestigationAgentRunner.PromptId].Version,
        release.ActiveChatDeployment,
        release.KnowledgeIndex);
}
