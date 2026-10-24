using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Retrieval;

/// <param name="Answerable">N for recall@5 and MRR: the cases with expected sections.</param>
internal sealed record RetrievalResult(RunMetrics Metrics, int Answerable, IReadOnlyList<CaseRun> Cases);

/// <summary>
/// The held-out retrieval cases in the release's own mode and floor, one run (lexical retrieval is deterministic).
/// Any Restricted section returned under General access is a hard-gate leak.
/// </summary>
internal static class RetrievalEval
{
    public static async Task<RetrievalResult> RunAsync(
        CalibrationDataset dataset,
        IReadOnlyDictionary<ExpectedSection, string> keys,
        AiRelease release,
        Func<string, KnowledgeAccess, RetrievalMode, Task<IReadOnlyList<RetrievedSection>>> retrieve)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(retrieve);

        var mode = release.RetrievalMode.ToString().ToLowerInvariant();
        var runs = new List<CaseRun>();
        foreach (var testCase in dataset.Cases)
        {
            var hits = await retrieve(testCase.Question, KnowledgeAccess.From(CalibrationUser.For(testCase.Access)), release.RetrievalMode);
            var expected = testCase.Expected.Select(e => keys[e]).ToHashSet(StringComparer.Ordinal);
            runs.Add(CalibrationDecision.Score(testCase, mode, [.. hits.Select(h => new CaseHit(h.Key, h.DocId, h.SectionPath, h.Classification, h.RerankerScore))], expected));
        }

        var answerable = runs.Where(run => CalibrationCase.Answerable(run.Kind)).ToList();
        double? Ratio(double sum) => answerable.Count == 0 ? null : sum / answerable.Count;
        var metrics = new RunMetrics(
            new Dictionary<string, double?>(StringComparer.Ordinal)
            {
                [MetricNames.RetrievalRecallAt5] = Ratio(answerable.Count(run => run.ExpectedRank is <= KnowledgeRetriever.Top)),
                [MetricNames.RetrievalMrr] = Ratio(answerable.Sum(run => run.ReciprocalRank)),
            },
            new HardGates([.. runs.Where(run => run.RestrictedLeak).Select(run => new Violation(run.CaseId, "a Restricted section was returned under General access"))], []),
            runs.ToDictionary(run => run.CaseId, run => (IReadOnlyDictionary<string, bool>)new Dictionary<string, bool> { [MetricNames.RetrievalRecallAt5] = run.ExpectedRank is not null }, StringComparer.Ordinal));
        return new RetrievalResult(metrics, answerable.Count, runs);
    }
}
