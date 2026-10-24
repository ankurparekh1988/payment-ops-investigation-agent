using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.AI;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Prompts;
using PaymentOps.Evaluation.Running;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Judging;

/// <summary>
/// Three 1-5 scores for one result, recorded and never gating. <see cref="Reason"/> is this harness's own note on
/// why a case wasn't scored; the judge's words are never kept, so reports carry no model text.
/// </summary>
internal sealed record JudgeScore(int? Groundedness, int? Relevance, int? RootCauseAgreement, string Status, string? Reason)
{
    public const string Scored = "scored";
    public const string NotScored = "not-scored";

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public static JudgeScore Skipped(string reason) => new(null, null, null, NotScored, reason);
}

/// <summary>
/// Scores a creditable result on an evidence-scored case with the pinned judge deployment. It reads the server's
/// own records and section text for what the assessment cited, marked as data. A judge that fails or answers out
/// of range leaves the case unscored; it never fails the run.
/// </summary>
internal sealed class Judge(IChatClient judgeModel)
{
    public static EvaluationPrompt Prompt { get; } = EvaluationPrompt.Load("prompts/judge.md");

    private static readonly JsonSerializerOptions Strict = new(ToolJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public async Task<JudgeScore> ScoreAsync(InvestigationCase c, CaseResult r, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(r);

        if (!c.ScoreEvidence || !c.CreditableOutcomes.Contains(r.Outcome))
        {
            return JudgeScore.Skipped("not a creditable result on an evidence-scored case");
        }

        ChatResponse<JudgeVerdict> response;
        try
        {
            response = await judgeModel.GetResponseAsync<JudgeVerdict>(
                [new ChatMessage(ChatRole.System, Prompt.Body), new ChatMessage(ChatRole.User, Input(c, r))],
                Strict,
                new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return JudgeScore.Skipped($"the judge call failed ({ex.GetType().Name})");
        }

        var tokens = (Input: response.Usage?.InputTokenCount ?? 0, Output: response.Usage?.OutputTokenCount ?? 0);
        if (!TryResult(response, out var verdict) || !InRange(verdict.Groundedness, verdict.Relevance, verdict.RootCauseAgreement))
        {
            return JudgeScore.Skipped("the judge's answer wasn't three scores from 1 to 5") with { InputTokens = tokens.Input, OutputTokens = tokens.Output };
        }

        return new JudgeScore(verdict.Groundedness, verdict.Relevance, verdict.RootCauseAgreement, JudgeScore.Scored, null) with { InputTokens = tokens.Input, OutputTokens = tokens.Output };
    }

    private static bool TryResult(ChatResponse<JudgeVerdict> response, out JudgeVerdict verdict)
    {
        try
        {
            return response.TryGetResult(out verdict!) && verdict is not null;
        }
        catch (JsonException)
        {
            verdict = null!;
            return false;
        }
    }

    private static bool InRange(params int[] scores) => scores.All(s => s is >= 1 and <= 5);

    // The assessment, records and sections are all material to score, so each is marked as data.
    private static string Input(InvestigationCase c, CaseResult r)
    {
        var input = new StringBuilder();
        input.AppendLine("Question:").AppendLine(c.Question).AppendLine();
        input.AppendLine("Expected root cause, written by a reviewer:").AppendLine(c.ExpectedRootCause).AppendLine();
        input.AppendLine("The answer:").AppendLine(UntrustedText.Wrap(r.AssessmentText)).AppendLine();
        input.AppendLine("Records the answer cited as evidence:");
        foreach (var fact in r.CitedEvidenceFacts.Values)
        {
            input.AppendLine(UntrustedText.Wrap(fact));
        }

        input.AppendLine().AppendLine("Knowledge sections the answer cited:");
        foreach (var section in r.CitedSectionText.Values)
        {
            input.AppendLine(UntrustedText.Wrap(section));
        }

        return input.ToString();
    }

    private sealed record JudgeVerdict(int Groundedness, int Relevance, int RootCauseAgreement, string Reason);
}
