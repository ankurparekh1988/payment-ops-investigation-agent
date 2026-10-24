using System.Text.Json;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Running;

namespace PaymentOps.Evaluation.Tests;

public sealed class JudgeTests
{
    private const string Verdict = """{"groundedness":4,"relevance":5,"root_cause_agreement":3,"reason":"Mostly supported."}""";

    private static readonly InvestigationCase Eligible = EvaluationFakes.Case() with { ScoreEvidence = true, ExpectedRootCause = "Fabrikam's 9 November file never arrived." };

    private static readonly CaseResult Creditable = new("c1", "assessed")
    {
        AssessmentText = "The 9 November Fabrikam file never arrived.",
        UserVisibleText = "The 9 November Fabrikam file never arrived.\nTICKET TITLE SENTINEL",
        CitedEvidenceFacts = new Dictionary<string, string> { ["fact:missing-file:fabrikam-pay:2026-11-09"] = """{"evidence":{"id":"fact:missing-file:fabrikam-pay:2026-11-09"}}""" },
        CitedSectionText = new Dictionary<string, string> { ["k1"] = "Raise an incident with the processor when a file is missing." },
    };

    private static ScriptedChatClient Model(params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] script) => new(script);

    private static ChatResponse Text(string text) => ScriptedChatClient.Text(text);

    [Fact]
    public async Task A_creditable_result_gets_three_scores()
    {
        var score = await new Judge(Model(_ => Text(Verdict))).ScoreAsync(Eligible, Creditable, CancellationToken.None);

        Assert.Equal(new JudgeScore(4, 5, 3, JudgeScore.Scored, null) { InputTokens = 0, OutputTokens = 0 }, score);
    }

    [Fact]
    public async Task A_failing_judge_leaves_the_case_unscored_and_the_run_intact()
    {
        var throwing = await new Judge(Model(_ => throw new HttpRequestException("503"))).ScoreAsync(Eligible, Creditable, CancellationToken.None);
        var outOfRange = await new Judge(Model(_ => Text("""{"groundedness":9,"relevance":5,"root_cause_agreement":3,"reason":"x"}"""))).ScoreAsync(Eligible, Creditable, CancellationToken.None);
        var malformed = await new Judge(Model(_ => Text("not json"))).ScoreAsync(Eligible, Creditable, CancellationToken.None);

        Assert.All([throwing, outOfRange, malformed], s =>
        {
            Assert.Equal(JudgeScore.NotScored, s.Status);
            Assert.Null(s.Groundedness);
        });
    }

    [Fact]
    public async Task A_non_creditable_result_is_not_sent_to_the_judge()
    {
        var model = Model(_ => Text(Verdict));
        var judge = new Judge(model);

        var incomplete = await judge.ScoreAsync(Eligible, Creditable with { Outcome = "incomplete" }, CancellationToken.None);
        var ineligible = await judge.ScoreAsync(Eligible with { ScoreEvidence = false }, Creditable, CancellationToken.None);

        Assert.Empty(model.Requests);
        Assert.Equal(JudgeScore.NotScored, incomplete.Status);
        Assert.Equal(JudgeScore.NotScored, ineligible.Status);
    }

    [Fact]
    public async Task The_judge_sees_the_cited_facts_and_sections_as_untrusted_data()
    {
        var model = Model(_ => Text(Verdict));

        await new Judge(model).ScoreAsync(Eligible, Creditable, CancellationToken.None);

        var request = Assert.Single(model.Requests);
        var text = string.Join('\n', request.Messages.Select(m => m.Text));
        Assert.Contains("<untrusted-data>{\"evidence\":{\"id\":\"fact:missing-file:fabrikam-pay:2026-11-09\"}}</untrusted-data>", text, StringComparison.Ordinal);
        Assert.Contains("<untrusted-data>Raise an incident with the processor when a file is missing.</untrusted-data>", text, StringComparison.Ordinal);
        Assert.Contains(Eligible.ExpectedRootCause!, text, StringComparison.Ordinal);
        Assert.Contains(Eligible.Question, text, StringComparison.Ordinal);
        Assert.DoesNotContain("TICKET TITLE SENTINEL", text, StringComparison.Ordinal);
        Assert.Contains("version: 1.0.0", Judge.Prompt.Header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_judge_score_never_serializes_the_judges_own_words()
    {
        var score = new JudgeScore(4, 5, 3, JudgeScore.Scored, null);

        Assert.DoesNotContain("Mostly supported", JsonSerializer.Serialize(score), StringComparison.Ordinal);
    }
}
