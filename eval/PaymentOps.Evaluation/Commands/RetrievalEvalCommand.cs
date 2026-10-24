using Azure.Identity;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

namespace PaymentOps.Evaluation.Commands;

/// <summary>
/// retrieval-eval: the held-out retrieval cases against the live index in the release's mode, query only.
/// It calls no model and writes nothing to Search.
/// </summary>
internal static class RetrievalEvalCommand
{
    public const string Usage = "retrieval-eval --endpoint <url> --ai <dir> --sources <dir> --dataset <file> [--thresholds <file>] --out <file> --summary <file>";

    private static readonly string[] Valued = ["endpoint", "ai", "sources", "dataset", "thresholds", "out", "summary"];

    public static Task<int> RunAsync(string[] args) => ExitCodes.GuardAsync("Retrieval evaluation", Usage, async () =>
    {
        var options = Options.Parse(args, Valued, []);
        var endpoint = options.Https("endpoint");
        var thresholdsPath = options.Optional("thresholds");
        var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(options.One("ai")));
        var dataset = CalibrationDataset.Load(options.One("dataset"));
        var keys = CalibrationReferences.Resolve(dataset, IndexedCorpus.FromSourceDirectory(options.One("sources")));
        var thresholds = thresholdsPath is null ? null : Thresholds.Load(thresholdsPath);
        if (thresholds is not null && dataset.Cases.Any(c => c.IsAnswerable))
        {
            Gate.Require(thresholds, [MetricNames.RetrievalRecallAt5]);
        }

        var retriever = KnowledgeRetriever.Create(endpoint, release.KnowledgeIndex, new AzureCliCredential());
        var startedAt = DateTimeOffset.UtcNow;
        var result = await OnceMoreIfUnavailableAsync(() => RetrievalEval.RunAsync(dataset, keys, release, (question, access, mode) =>
            retriever.RetrieveAsync(question, access, mode, release.SemanticFloor, CancellationToken.None)));

        var context = new ReportContext(EvaluationReport.RetrievalSystem, RepositoryCommit.Current(), [], null, null, null, startedAt);
        var report = EvaluationReport.ForRetrieval(context, result, thresholds, thresholdsPath);
        await File.WriteAllTextAsync(options.One("out"), report.ToJson());
        await File.AppendAllTextAsync(options.One("summary"), ReportMarkdown.Retrieval(report));
        if (report.Gate.Verdict == GateReport.Failed)
        {
            GitHubAnnotation.Errors("Retrieval evaluation", report.Gate.Failures);
            return ExitCodes.Failed;
        }

        return ExitCodes.Passed;
    });

    // Search failing is the environment, not retrieval: the one run is repeated once, as an investigation run is.
    private static async Task<RetrievalResult> OnceMoreIfUnavailableAsync(Func<Task<RetrievalResult>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception first) when (InfrastructureFailures.Of(first) is not null)
        {
            Console.Error.WriteLine($"The retrieval run failed ({InfrastructureFailures.Of(first)}); repeating it once.");
            await Task.Delay(TimeSpan.FromMinutes(1));
            try
            {
                return await run();
            }
            catch (Exception second) when (InfrastructureFailures.Of(second) is { } failure)
            {
                throw new CouldNotEvaluateException($"The retrieval run failed twice ({failure}). The evaluation environment is unavailable.");
            }
        }
    }
}
