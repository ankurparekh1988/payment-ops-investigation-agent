// Evaluation harness. retrieval-compare runs the retrieval calibration against the live index,
// query only: it writes nothing to Search, calls no model and never edits the AI release. Signs in
// with the Azure CLI session; Search has key authentication disabled.
//
// Usage: PaymentOps.Evaluation retrieval-compare --endpoint <url> --ai <dir> --dataset <file> --sources <dir> --out <file> --summary <file>

using System.Globalization;

using Azure.Identity;

using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

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
