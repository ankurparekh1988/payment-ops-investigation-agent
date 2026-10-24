using System.Globalization;

using Azure.Identity;

using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

namespace PaymentOps.Evaluation.Commands;

/// <summary>
/// retrieval-compare: the retrieval calibration in both modes against the live index, query only. It recommends a
/// mode and floor and never edits the AI release; it shares the other commands' options and exit codes.
/// </summary>
internal static class RetrievalCompareCommand
{
    public const string Usage = "retrieval-compare --endpoint <url> --ai <dir> --dataset <file> --sources <dir> --out <file> --summary <file>";

    private static readonly string[] Valued = ["endpoint", "ai", "dataset", "sources", "out", "summary"];

    public static Task<int> RunAsync(string[] args) => ExitCodes.GuardAsync("Retrieval calibration", Usage, async () =>
    {
        var options = Options.Parse(args, Valued, []);
        var endpoint = options.Https("endpoint");
        var (outPath, summaryPath) = (options.OutputFile("out"), options.OutputFile("summary"));
        var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(options.One("ai")));
        var dataset = CalibrationDataset.Load(options.One("dataset"));
        var keys = CalibrationReferences.Resolve(dataset, IndexedCorpus.FromSourceDirectory(options.One("sources")));

        // In the pipeline, azure/login has signed the CLI in as the deploy identity through OIDC.
        var retriever = KnowledgeRetriever.Create(endpoint, release.KnowledgeIndex, new AzureCliCredential());
        var result = await RunPlan.OnceMoreIfInvalidAsync(
            "The calibration run",
            () => RetrievalCompare.RunAsync(dataset, keys, (question, access, mode) => retriever.RetrieveAsync(question, access, mode, null, CancellationToken.None)),
            _ => null,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        await File.WriteAllTextAsync(outPath, CalibrationReport.ToJson(result));
        await File.AppendAllTextAsync(summaryPath, CalibrationReport.ToMarkdown(result));
        if (!result.ProbesPassed)
        {
            GitHubAnnotation.Error("Retrieval calibration", "A Restricted section was returned under General access.");
            return ExitCodes.Failed;
        }

        var floor = result.RecommendedFloor?.ToString("F2", CultureInfo.InvariantCulture) ?? "none";
        Console.WriteLine($"Recommended mode: {result.RecommendedMode}; floor: {floor}.");
        return ExitCodes.Passed;
    });
}
