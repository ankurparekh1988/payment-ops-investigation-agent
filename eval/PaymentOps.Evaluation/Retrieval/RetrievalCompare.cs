using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Retrieval;

/// <summary>Runs every case in both modes with no floor; the decision rule chooses afterwards.</summary>
internal static class RetrievalCompare
{
    public static async Task<CalibrationResult> RunAsync(
        CalibrationDataset dataset,
        IReadOnlyDictionary<ExpectedSection, string> keys,
        Func<string, KnowledgeAccess, RetrievalMode, Task<IReadOnlyList<RetrievedSection>>> retrieve)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(retrieve);

        // In series: a few dozen queries, and the free search plan throttles bursts.
        var runs = new List<CaseRun>();
        foreach (var testCase in dataset.Cases)
        {
            foreach (var mode in new[] { RetrievalMode.Lexical, RetrievalMode.Semantic })
            {
                runs.Add(await RunCaseAsync(testCase, keys, mode, retrieve));
            }
        }

        return CalibrationDecision.Decide(runs);
    }

    /// <summary>One case in one mode, as the case's synthetic user, scored against its expected sections.</summary>
    public static async Task<CaseRun> RunCaseAsync(
        CalibrationCase testCase,
        IReadOnlyDictionary<ExpectedSection, string> keys,
        RetrievalMode mode,
        Func<string, KnowledgeAccess, RetrievalMode, Task<IReadOnlyList<RetrievedSection>>> retrieve)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(retrieve);

        var hits = await retrieve(testCase.Question, KnowledgeAccess.From(CalibrationUser.For(testCase.Access)), mode);
        var expected = testCase.Expected.Select(e => keys[e]).ToHashSet(StringComparer.Ordinal);
        var name = mode == RetrievalMode.Semantic ? CalibrationDecision.Semantic : CalibrationDecision.Lexical;
        return CalibrationDecision.Score(testCase, name, [.. hits.Select(h => new CaseHit(h.Key, h.DocId, h.SectionPath, h.Classification, h.RerankerScore))], expected);
    }
}
