using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Evaluation.Retrieval;

/// <summary>
/// The frozen rule that turns calibration runs into a recommendation. It recommends; it never edits
/// the release. A Restricted hit under General access fails the run regardless of ranking.
/// </summary>
internal static class CalibrationDecision
{
    public const string Lexical = "lexical";
    public const string Semantic = "semantic";
    public const string Escalate = "escalate";
    public const string Failed = "failed";

    /// <summary>When both modes pass, semantic has to beat lexical's MRR by at least this much.</summary>
    public const double MaterialMrrGain = 0.10;

    public static CaseRun Score(CalibrationCase testCase, string mode, IReadOnlyList<CaseHit> top, IReadOnlySet<string> expectedKeys)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(top);

        var index = top.ToList().FindIndex(hit => expectedKeys.Contains(hit.Key));
        int? rank = index < 0 ? null : index + 1;
        var leak = testCase.Access == KnowledgeClassification.General && top.Any(hit => hit.Classification == KnowledgeClassification.Restricted);
        return new CaseRun(testCase.Id, testCase.Kind, mode, top, rank, rank is { } r ? 1.0 / r : 0, leak)
        {
            ExpectedScores = [.. top.Where(hit => expectedKeys.Contains(hit.Key) && hit.RerankerScore is not null).Select(hit => hit.RerankerScore!.Value)],
        };
    }

    public static CalibrationResult Decide(IReadOnlyList<CaseRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var probesPassed = !runs.Any(run => run.RestrictedLeak);
        var lexical = Summarize(Lexical, runs);
        var semantic = Summarize(Semantic, runs);
        var candidates = FloorCandidates([.. runs.Where(run => run.Mode == Semantic)]);

        var recommended = !probesPassed ? Failed : (lexical.PassesCriticalBar, semantic.PassesCriticalBar) switch
        {
            (false, false) => Escalate,
            (true, false) => Lexical,
            (false, true) => Semantic,
            _ => semantic.Mrr >= lexical.Mrr + MaterialMrrGain ? Semantic : Lexical,
        };

        var floor = recommended == Semantic ? candidates.Where(c => c.Qualifies).Select(c => (double?)c.Floor).Max() : null;
        return new CalibrationResult(runs, [lexical, semantic], probesPassed, recommended, floor, candidates);
    }

    private static ModeSummary Summarize(string mode, IReadOnlyList<CaseRun> runs)
    {
        var inMode = runs.Where(run => run.Mode == mode).ToList();
        var critical = inMode.Where(run => run.Kind == CaseKind.Critical).ToList();
        var misses = critical.Where(run => run.ExpectedRank is null).Select(run => run.CaseId).ToList();
        var answerable = inMode.Where(run => CalibrationCase.Answerable(run.Kind)).ToList();
        var mrr = answerable.Count == 0 ? 0 : answerable.Average(run => run.ReciprocalRank);
        return new ModeSummary(mode, critical.Count > 0 && misses.Count == 0, misses, mrr);
    }

    // 0.25, 0.50, ... 4.00 on the reranker's 0-4 scale. Every answerable case must keep an expected
    // section at the floor, so one that semantic already missed rules out every floor.
    private static List<FloorCandidate> FloorCandidates(IReadOnlyList<CaseRun> semantic)
    {
        var answerable = semantic.Where(run => CalibrationCase.Answerable(run.Kind)).ToList();
        var unrelated = semantic.Where(run => run.Kind == CaseKind.Unrelated).ToList();
        return [.. Enumerable.Range(1, 16).Select(step => step * 0.25).Select(floor =>
        {
            var keeps = answerable.All(run => run.ExpectedScores.Any(score => score >= floor));
            var emptied = unrelated.Count(run => run.Top.All(hit => (hit.RerankerScore ?? 0) < floor));
            return new FloorCandidate(floor, keeps, emptied, unrelated.Count, keeps && unrelated.Count > 0 && emptied * 2 >= unrelated.Count);
        })];
    }
}
