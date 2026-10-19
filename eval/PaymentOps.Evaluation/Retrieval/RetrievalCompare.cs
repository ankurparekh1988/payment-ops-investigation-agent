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

        var runs = new List<CaseRun>();
        foreach (var testCase in dataset.Cases)
        {
            var access = KnowledgeAccess.From(CalibrationUser.For(testCase.Access));
            var expected = testCase.Expected.Select(e => keys[e]).ToHashSet(StringComparer.Ordinal);
            foreach (var (mode, name) in new[] { (RetrievalMode.Lexical, CalibrationDecision.Lexical), (RetrievalMode.Semantic, CalibrationDecision.Semantic) })
            {
                var hits = await retrieve(testCase.Question, access, mode);
                runs.Add(CalibrationDecision.Score(testCase, name, [.. hits.Select(h => new CaseHit(h.Key, h.DocId, h.SectionPath, h.Classification, h.RerankerScore))], expected));
            }
        }

        return CalibrationDecision.Decide(runs);
    }
}
