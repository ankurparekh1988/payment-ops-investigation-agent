using System.Globalization;
using System.Text;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Conversations;

/// <summary>
/// Earlier turns as plain text for the next question: no tool messages and no section ids, so nothing in them
/// can be cited. Record ids stay as references the agent can look up again; sections become their titles.
/// </summary>
internal static class HistoryProjection
{
    public const string Marker = "Earlier context, not evidence for this question.";

    /// <summary>What's left for history after the current turn's instructions, question, tool results and output are reserved.</summary>
    public static int HistoryBudgetChars(ConversationOptions options, InvestigationLimits limits, int fixedPromptChars)
    {
        var reserved = PromptSize.Tokens(fixedPromptChars) + PromptSize.Tokens(limits.MaxQuestionLength)
            + (limits.MaxToolCalls * options.ToolResultAllowanceTokens) + limits.MaxOutputTokens;
        return Math.Max(0, options.InputBudgetTokens - reserved) * PromptSize.CharsPerToken;
    }

    /// <summary>The newest turns that fit the budget, oldest first, two messages each. Stops at the first turn that doesn't fit.</summary>
    public static IReadOnlyList<ChatMessage> Build(IReadOnlyList<ConversationTurn> turns, int budgetChars, Func<Guid, string?> approvalNote)
    {
        var carried = new List<ChatMessage>();
        var used = 0;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            var turn = turns[i];
            var answer = Answer(turn.Result, approvalNote(turn.Result.InvestigationId));
            used += turn.Question.Length + answer.Length;
            if (used > budgetChars)
            {
                break;
            }

            carried.InsertRange(0, [new ChatMessage(ChatRole.User, turn.Question), new ChatMessage(ChatRole.Assistant, answer)]);
        }

        return carried;
    }

    private static string Answer(AssessmentResult result, string? note)
    {
        var text = new StringBuilder(Marker).AppendLine();
        switch (result)
        {
            case AssessmentResult.Assessed { Outcome: AssessmentOutcome.Refused }:
                text.AppendLine("The question was refused as outside what this agent investigates.");
                break;
            case AssessmentResult.Assessed a:
                text.AppendLine(CultureInfo.InvariantCulture, $"Outcome: {ToolJson.Word(a.Outcome)}. Confidence: {ToolJson.Word(a.Confidence)}.");
                text.Append("Summary: ").AppendLine(Line(a.Summary));
                foreach (var claim in a.Claims)
                {
                    text.Append("- ").AppendLine(Line(claim));
                }

                if (a.RootCause is { } root)
                {
                    text.Append(CultureInfo.InvariantCulture, $"Root cause ({ToolJson.Word(root.Label)}): ").AppendLine(Line(root.Statement));
                }

                foreach (var gap in a.Gaps)
                {
                    text.Append("Not checked: ").AppendLine(gap);
                }

                break;
            default:
                text.AppendLine("This question wasn't assessed.");
                break;
        }

        if (note is not null)
        {
            text.AppendLine(note);
        }

        return text.ToString().TrimEnd();
    }

    private static string Line(Statement statement)
    {
        var line = new StringBuilder(statement.Text);
        if (statement.Evidence.Count > 0)
        {
            line.Append(" (records: ").AppendJoin(", ", statement.Evidence.Select(e => e.Id)).Append(')');
        }

        foreach (var title in statement.Citations.Select(c => c.Title).Distinct(StringComparer.Ordinal))
        {
            line.Append(" (source: ").Append(title).Append(')');
        }

        return line.ToString();
    }
}
