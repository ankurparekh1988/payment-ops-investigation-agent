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

    /// <summary>
    /// Only an answered turn is context. A blocked, refused or unanswered question is never sent again, so an attack
    /// the model declined once can't ride along with every later question.
    /// </summary>
    public static bool IsCarried(AssessmentResult result) =>
        result is AssessmentResult.Assessed { Outcome: AssessmentOutcome.Assessed or AssessmentOutcome.Incomplete or AssessmentOutcome.Escalated };

    /// <summary>The newest carried turns that fit the budget, oldest first, two messages each. Stops at the first that doesn't fit.</summary>
    public static IReadOnlyList<ChatMessage> Build(IReadOnlyList<ConversationTurn> turns, int budgetChars, Func<Guid, string?> approvalNote) =>
        Carry(turns, budgetChars, approvalNote).Messages;

    /// <summary>As <see cref="Build"/>, and whether the newest carried turn alone didn't fit, which ends the conversation.</summary>
    public static (IReadOnlyList<ChatMessage> Messages, bool NewestDoesNotFit) Carry(IReadOnlyList<ConversationTurn> turns, int budgetChars, Func<Guid, string?> approvalNote)
    {
        var carried = new List<ChatMessage>();
        var used = 0;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            if (turns[i].Result is not AssessmentResult.Assessed assessed || !IsCarried(assessed))
            {
                continue;
            }

            var answer = Answer(assessed, approvalNote(assessed.InvestigationId));
            used += turns[i].Question.Length + answer.Length;
            if (used > budgetChars)
            {
                return (carried, carried.Count == 0);
            }

            carried.InsertRange(0, [new ChatMessage(ChatRole.User, turns[i].Question), new ChatMessage(ChatRole.Assistant, answer)]);
        }

        return (carried, false);
    }

    private static string Answer(AssessmentResult.Assessed a, string? note)
    {
        var text = new StringBuilder(Marker).AppendLine();
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
