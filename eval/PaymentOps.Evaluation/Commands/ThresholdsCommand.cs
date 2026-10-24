using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Commands;

/// <summary>
/// thresholds: applies the pre-registered rule to a three-run agent baseline and its retrieval run. The output is
/// the thresholds file exactly as frozen; it is never edited by hand.
/// </summary>
internal static class ThresholdsCommand
{
    public const string Usage = "thresholds --investigations <report.json> --retrieval <report.json> --out <file>";

    private const int BaselineRuns = 3;

    public static Task<int> RunAsync(string[] args) => ExitCodes.GuardAsync("Thresholds", Usage, async () =>
    {
        var options = Options.Parse(args, ["investigations", "retrieval", "out"], []);
        var outPath = options.OutputFile("out");
        var investigations = EvaluationReport.Load(options.One("investigations"));
        var retrieval = EvaluationReport.Load(options.One("retrieval"));

        Expect(investigations.System == EvaluationReport.AgentSystem && investigations.Runs.Count == BaselineRuns, $"The investigation baseline must be {BaselineRuns} runs of the agent.");
        Expect(retrieval.System == EvaluationReport.RetrievalSystem && retrieval.Runs.Count == 1, "The retrieval baseline must be one retrieval-eval run.");
        Expect(investigations.Commit == retrieval.Commit, "Both baseline reports must come from the same commit.");
        Expect(RepositoryCommit.IsClean(investigations.Commit), $"The baseline came from '{investigations.Commit}', not a committed tree; run it again from a clean checkout.");

        // A hard-gate failure in the baseline is a finding to fix and re-run, never a baseline; nor is a run that didn't work at all.
        Expect(investigations.Runs.Concat(retrieval.Runs).All(r => r.HardGates.Passed), "The baseline failed a hard gate; fix it and run the baseline again.");
        Expect(investigations.Runs.All(r => r.Metrics.GetValueOrDefault(MetricNames.ValidatedResultRate) > 0), $"A baseline run failed viability ({Gate.NotViable}); fix the system and run the baseline again.");

        var metrics = new Dictionary<string, ThresholdEntry>(StringComparer.Ordinal);
        foreach (var report in new[] { investigations, retrieval })
        {
            foreach (var (metric, summary) in report.Summary.Where(s => ThresholdRule.Gating.Contains(s.Key) && s.Value.N > 0))
            {
                var values = report.Runs.Select(r => r.Metrics.GetValueOrDefault(metric)).OfType<double>().ToList();
                metrics[metric] = new ThresholdEntry(
                    ThresholdRule.Derive(metric, values, summary.N)!.Value,
                    ThresholdRule.Direction(metric),
                    summary.N,
                    summary.Min!.Value,
                    summary.Median!.Value,
                    summary.Max!.Value);
            }
        }

        var thresholds = new Thresholds(Thresholds.SupportedSchemaVersion, investigations.Commit, ThresholdRule.Text, metrics);
        await File.WriteAllTextAsync(outPath, thresholds.ToJson());
        Console.WriteLine($"Thresholds for {metrics.Count} metrics from the baseline at {investigations.Commit}.");
        return ExitCodes.Passed;
    });

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
