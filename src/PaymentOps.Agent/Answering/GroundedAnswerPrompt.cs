using System.Text;

using Microsoft.Extensions.AI;

using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Answering;

/// <summary>
/// The system prompt from the release, and the question with its evidence in delimiters. Delimiters
/// help the model tell data from instructions; they aren't a security control.
/// </summary>
internal static class GroundedAnswerPrompt
{
    public const string PromptId = "grounded-answer";

    private static readonly Dictionary<string, string> NoValues = [];

    public static IList<ChatMessage> Build(Prompt prompt, string question, EvidenceRegistry registry)
    {
        var user = new StringBuilder();
        user.Append("<question>\n").Append(question).Append("\n</question>\n");
        foreach (var (id, section) in registry.Entries)
        {
            user.Append("<section id=\"").Append(id).Append("\">\n")
                .Append("Title: ").Append(section.Title).Append('\n')
                .Append("Path: ").Append(section.SectionPath).Append('\n')
                .Append(section.Content).Append("\n</section>\n");
        }

        return [new ChatMessage(ChatRole.System, prompt.Template.Render(NoValues)), new ChatMessage(ChatRole.User, user.ToString())];
    }
}
