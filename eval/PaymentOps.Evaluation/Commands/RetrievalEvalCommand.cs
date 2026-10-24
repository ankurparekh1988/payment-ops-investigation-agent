using Azure.Identity;

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

    public static Task<int> RunAsync(string[] args) => RunAsync(args, null, TimeSpan.FromMinutes(1));

    /// <param name="retrieve">Tests only: stands in for the live index.</param>
    public static Task<int> RunAsync(
        string[] args,
        Func<string, KnowledgeAccess, RetrievalMode, Task<IReadOnlyList<RetrievedSection>>>? retrieve,
        TimeSpan retryDelay) => ExitCodes.GuardAsync("Retrieval evaluation", Usage, async () =>
    {
        var options = Options.Parse(args, Valued, []);
        var endpoint = options.Https("endpoint");
        var thresholdsPath = options.Optional("thresholds");
        var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(options.One("ai")));
        var dataset = CalibrationDataset.Load(options.One("dataset"));
        var keys = CalibrationReferences.Resolve(dataset, IndexedCorpus.FromSourceDirectory(options.One("sources")));
        var thresholds = thresholdsPath is null ? null : Thresholds.Load(thresholdsPath);
        if (thresholds is not null)
        {
            Gate.Require(thresholds, EvaluationReport.Applicable(RetrievalEval.Opportunities(dataset)));
        }

        if (retrieve is null)
        {
            var retriever = KnowledgeRetriever.Create(endpoint, release.KnowledgeIndex, new AzureCliCredential());
            retrieve = (question, access, mode) => retriever.RetrieveAsync(question, access, mode, release.SemanticFloor, CancellationToken.None);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var result = await RunPlan.OnceMoreIfInvalidAsync("The retrieval run", () => RetrievalEval.RunAsync(dataset, keys, release, retrieve), _ => null, retryDelay, CancellationToken.None);

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
}
