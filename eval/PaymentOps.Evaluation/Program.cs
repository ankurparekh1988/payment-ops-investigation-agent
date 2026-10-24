// Evaluation harness. Every command signs in with the Azure CLI session; key authentication is disabled on
// every service. investigation-eval runs the labelled investigation cases against the deployed model and index
// and gates them on the frozen thresholds; retrieval-eval runs the held-out retrieval cases, query only;
// thresholds derives the thresholds file from a baseline; kill-test compares the agent with the fixed workflow.
// retrieval-compare runs the retrieval calibration in both modes and recommends one. None of them writes to
// Search or edits the AI release. Exit codes: 0 passed or report-only, 1 failed, 2 bad command or input,
// 3 the evaluation environment was unavailable.
//
// Usage: PaymentOps.Evaluation investigation-eval --foundry <url> --search <url> --scope <token scope> --ai <dir> --sources <dir> --dataset <file> [--dataset <file>] --runs <n> --system agent|fixed-workflow --pricing <file> [--thresholds <file>] [--no-judge] --out <file> --summary <file>
//        PaymentOps.Evaluation retrieval-eval --endpoint <url> --ai <dir> --sources <dir> --dataset <file> [--thresholds <file>] --out <file> --summary <file>
//        PaymentOps.Evaluation thresholds --investigations <report.json> --retrieval <report.json> --out <file>
//        PaymentOps.Evaluation kill-test --agent <report.json> --challenger <report.json> --out <file> --summary <file>
//        PaymentOps.Evaluation retrieval-compare --endpoint <url> --ai <dir> --dataset <file> --sources <dir> --out <file> --summary <file>

using System.Globalization;

using Azure.Identity;

using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

switch (args)
{
    case ["investigation-eval", .. var rest]:
        return await InvestigationEvalCommand.RunAsync(rest);
    case ["retrieval-eval", .. var rest]:
        return await RetrievalEvalCommand.RunAsync(rest);
    case ["thresholds", .. var rest]:
        return await ThresholdsCommand.RunAsync(rest);
    case ["kill-test", .. var rest]:
        return await KillTestCommand.RunAsync(rest);
}

if (args is not ["retrieval-compare", "--endpoint", var url, "--ai", var ai, "--dataset", var datasetPath, "--sources", var sources, "--out", var outPath, "--summary", var summaryPath]
    || !Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
{
    Console.Error.WriteLine("Usage: PaymentOps.Evaluation retrieval-compare --endpoint <url> --ai <dir> --dataset <file> --sources <dir> --out <file> --summary <file>");
    return 2;
}

try
{
    var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(ai));
    var dataset = CalibrationDataset.Load(datasetPath);
    var keys = CalibrationReferences.Resolve(dataset, IndexedCorpus.FromSourceDirectory(sources));

    // In the pipeline, azure/login has signed the CLI in as the deploy identity through OIDC.
    var retriever = KnowledgeRetriever.Create(endpoint, release.KnowledgeIndex, new AzureCliCredential());
    var result = await RetrievalCompare.RunAsync(dataset, keys, (question, access, mode) => retriever.RetrieveAsync(question, access, mode, null, CancellationToken.None));

    await File.WriteAllTextAsync(outPath, CalibrationReport.ToJson(result));
    await File.AppendAllTextAsync(summaryPath, CalibrationReport.ToMarkdown(result));

    if (!result.ProbesPassed)
    {
        GitHubAnnotation.Error("Retrieval calibration", "A Restricted section was returned under General access.");
        return 1;
    }

    var floor = result.RecommendedFloor?.ToString("F2", CultureInfo.InvariantCulture) ?? "none";
    Console.WriteLine($"Recommended mode: {result.RecommendedMode}; floor: {floor}.");
    return 0;
}
catch (AiReleaseException ex)
{
    GitHubAnnotation.Errors("AI release", ex.Errors);
    return 1;
}
catch (KnowledgeCorpusException ex)
{
    GitHubAnnotation.Errors("Knowledge corpus", ex.Errors);
    return 1;
}
catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KnowledgeUnavailableException or AuthenticationFailedException)
{
    GitHubAnnotation.Error("Retrieval calibration", ex.Message);
    return 1;
}
