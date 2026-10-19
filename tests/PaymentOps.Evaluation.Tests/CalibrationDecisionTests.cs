using PaymentOps.Evaluation.Retrieval;

namespace PaymentOps.Evaluation.Tests;

public sealed class CalibrationDecisionTests
{
    [Fact]
    public void Rank_and_reciprocal_rank_use_the_first_expected_section()
    {
        var run = CalibrationDecision.Score(new CalibrationCase("c", CaseKind.Critical, "general", "q", []), CalibrationDecision.Lexical,
            [Hit("x"), Hit("e2"), Hit("e1")], new HashSet<string>(StringComparer.Ordinal) { "e1", "e2" });

        Assert.Equal(2, run.ExpectedRank);
        Assert.Equal(0.5, run.ReciprocalRank);
    }

    [Fact]
    public void A_miss_has_no_rank_and_zero_reciprocal_rank()
    {
        var run = CalibrationDecision.Score(new CalibrationCase("c", CaseKind.Critical, "general", "q", []), CalibrationDecision.Lexical,
            [Hit("x")], new HashSet<string>(StringComparer.Ordinal) { "e1" });

        Assert.Null(run.ExpectedRank);
        Assert.Equal(0, run.ReciprocalRank);
    }

    [Fact]
    public void Expected_scores_cover_every_expected_hit()
    {
        var run = CalibrationDecision.Score(new CalibrationCase("c", CaseKind.Paraphrase, "general", "q", []), CalibrationDecision.Semantic,
            [Hit("e2", score: 1.5), Hit("x", score: 3.0), Hit("e1", score: 2.5)], new HashSet<string>(StringComparer.Ordinal) { "e1", "e2" });

        Assert.Equal([1.5, 2.5], run.ExpectedScores);
    }

    [Fact]
    public void A_restricted_hit_under_general_access_is_a_leak()
    {
        var run = CalibrationDecision.Score(new CalibrationCase("p", CaseKind.Probe, "general", "q", []), CalibrationDecision.Semantic,
            [Hit("r", classification: "restricted", score: 3.0)], new HashSet<string>(StringComparer.Ordinal));

        Assert.True(run.RestrictedLeak);
    }

    [Fact]
    public void A_restricted_hit_under_restricted_access_is_not_a_leak()
    {
        var run = CalibrationDecision.Score(new CalibrationCase("c", CaseKind.Critical, "restricted", "q", []), CalibrationDecision.Lexical,
            [Hit("r", classification: "restricted")], new HashSet<string>(StringComparer.Ordinal) { "r" });

        Assert.False(run.RestrictedLeak);
    }

    [Fact]
    public void Any_leak_fails_the_run_whatever_the_ranks()
    {
        var result = CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 1, 1), Run("p1", CaseKind.Probe, CalibrationDecision.Semantic, null, leak: true)]);

        Assert.False(result.ProbesPassed);
        Assert.Equal(CalibrationDecision.Failed, result.RecommendedMode);
    }

    [Fact]
    public void One_critical_miss_fails_a_mode()
    {
        var result = CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 1, 1), .. Both("c2", CaseKind.Critical, null, 2)]);

        Assert.False(result.Modes.Single(m => m.Mode == CalibrationDecision.Lexical).PassesCriticalBar);
        Assert.Equal(["c2"], result.Modes.Single(m => m.Mode == CalibrationDecision.Lexical).CriticalMisses);
    }

    [Fact]
    public void Only_lexical_passes_lexical() =>
        Assert.Equal(CalibrationDecision.Lexical, CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 1, null)]).RecommendedMode);

    [Fact]
    public void Only_semantic_passes_semantic_without_needing_the_margin() =>
        Assert.Equal(CalibrationDecision.Semantic, CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, null, 5, [0.5, 0.5, 0.5, 0.5, 3.0])]).RecommendedMode);

    [Fact]
    public void Neither_passes_escalates() =>
        Assert.Equal(CalibrationDecision.Escalate, CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, null, null)]).RecommendedMode);

    // Lexical MRR (1/2 + 1/4)/2 = 0.375; semantic (1/2 + 1/3)/2 = 0.417: a gain of 0.042.
    [Fact]
    public void Both_pass_with_a_small_gain_keeps_lexical() =>
        Assert.Equal(CalibrationDecision.Lexical, CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 2, 2), .. Both("c2", CaseKind.Paraphrase, 4, 3)]).RecommendedMode);

    // Lexical MRR (1/2 + 1)/2 = 0.75; semantic (1 + 1)/2 = 1.0: a gain of 0.25.
    [Fact]
    public void Both_pass_with_a_gain_of_at_least_a_tenth_picks_semantic() =>
        Assert.Equal(CalibrationDecision.Semantic, CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 2, 1), .. Both("c2", CaseKind.Paraphrase, 1, 1)]).RecommendedMode);

    [Fact]
    public void Adjacent_and_unrelated_cases_dont_count_towards_mrr()
    {
        var result = CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 1, 1), .. Both("a1", CaseKind.Adjacent, null, null), .. Both("u1", CaseKind.Unrelated, null, null)]);

        Assert.All(result.Modes, m => Assert.Equal(1.0, m.Mrr));
    }

    [Fact]
    public void The_floor_is_the_highest_step_that_keeps_answerable_hits_and_empties_half_the_unrelated()
    {
        // The answerable case's expected hit scores 2.6; unrelated tops are 1.1 and 2.9, so 2.5 empties one of two.
        var result = CalibrationDecision.Decide(
        [
            Run("c1", CaseKind.Critical, CalibrationDecision.Lexical, null),
            Run("c1", CaseKind.Critical, CalibrationDecision.Semantic, 1, [2.6]),
            Run("u1", CaseKind.Unrelated, CalibrationDecision.Semantic, null, [1.1]),
            Run("u2", CaseKind.Unrelated, CalibrationDecision.Semantic, null, [2.9]),
        ]);

        Assert.Equal(CalibrationDecision.Semantic, result.RecommendedMode);
        Assert.Equal(2.5, result.RecommendedFloor);
    }

    [Fact]
    public void No_qualifying_floor_means_none()
    {
        var result = CalibrationDecision.Decide(
        [
            Run("c1", CaseKind.Critical, CalibrationDecision.Lexical, null),
            Run("c1", CaseKind.Critical, CalibrationDecision.Semantic, 1, [1.0]),
            Run("u1", CaseKind.Unrelated, CalibrationDecision.Semantic, null, [3.5]),
            Run("u2", CaseKind.Unrelated, CalibrationDecision.Semantic, null, [3.6]),
        ]);

        Assert.Equal(CalibrationDecision.Semantic, result.RecommendedMode);
        Assert.Null(result.RecommendedFloor);
    }

    [Fact]
    public void Lexical_gets_no_floor()
    {
        var result = CalibrationDecision.Decide([.. Both("c1", CaseKind.Critical, 1, null, [3.0]), Run("u1", CaseKind.Unrelated, CalibrationDecision.Semantic, null, [0.1])]);

        Assert.Equal(CalibrationDecision.Lexical, result.RecommendedMode);
        Assert.Null(result.RecommendedFloor);
    }

    private static CaseHit Hit(string key, string classification = "general", double? score = null) => new(key, "doc", "Doc > Section", classification, score);

    private static CaseRun Run(string id, CaseKind kind, string mode, int? rank, double[]? scores = null, bool leak = false) =>
        new(id, kind, mode, [.. (scores ?? []).Select((s, i) => Hit($"k{i}", score: s))], rank, rank is { } r ? 1.0 / r : 0, leak)
        {
            ExpectedScores = rank is { } at && scores is not null && scores.Length >= at ? [scores[at - 1]] : [],
        };

    private static CaseRun[] Both(string id, CaseKind kind, int? lexical, int? semantic, double[]? semanticScores = null) =>
        [Run(id, kind, CalibrationDecision.Lexical, lexical), Run(id, kind, CalibrationDecision.Semantic, semantic, semanticScores)];
}
