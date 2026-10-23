using Microsoft.Extensions.AI;

using PaymentOps.Agent.Conversations;
using PaymentOps.Domain.Investigation;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// What every request carries before history and the question: the instructions and every tool definition.
/// The one measurement both the startup budget check and the run-time history budget use.
/// </summary>
internal sealed class FixedPrompt(IEnumerable<IInvestigationTool> tools, IPromptProvider prompts, IInvestigationClock clock)
{
    private static readonly Dictionary<string, string> NoValues = [];
    private int? _chars;

    // The current time comes from the investigation clock, so "yesterday" is the data's yesterday, not the model's.
    public string Instructions() =>
        $"{prompts.GetPrompt(InvestigationAgentRunner.PromptId).Template.Render(NoValues)}\n\nNow: {clock.Now:yyyy-MM-dd HH:mm} UTC. Today is {clock.Today:yyyy-MM-dd}.";

    // Every tool, whoever asks: the largest request any user's turn can send.
    public int Chars => _chars ??= Instructions().Length
        + tools.Sum(tool => PromptSize.DefinitionChars(tool.Name, tool.Description, ToolGuard.Schema(tool.RequestType)))
        + PromptSize.DefinitionChars(new SubmitAssessmentFunction(new TurnState(), clock.Today, null));
}
