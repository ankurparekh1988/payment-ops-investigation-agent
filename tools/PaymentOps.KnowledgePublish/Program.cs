// Publishes a corpus build to the knowledge index the AI release names, then verifies that the index
// holds exactly that corpus. --self-test instead proves the publishing behaviours on a throwaway
// index. Signs in with the Azure CLI session; Search has key authentication disabled.
//
// --ensure-evaluation-index also makes sure the release's evaluation index exists, without documents.
// --documents-only publishes to the evaluation index instead, after proving the signed-in identity
// can't write the live index; it never creates or changes an index definition.
//
// Usage: PaymentOps.KnowledgePublish <corpus-directory> --endpoint <url> --ai <ai-directory> [--ensure-evaluation-index | --documents-only]
//        PaymentOps.KnowledgePublish --self-test --endpoint <url> --ai <ai-directory>

using Azure.Identity;

using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

(string? Corpus, string Endpoint, string Ai, string? Mode)? options = args switch
{
    ["--self-test", "--endpoint", var url, "--ai", var ai] => (null, url, ai, null),
    [var corpus, "--endpoint", var url, "--ai", var ai] => (corpus, url, ai, null),
    [var corpus, "--endpoint", var url, "--ai", var ai, var flag] when flag is "--ensure-evaluation-index" or "--documents-only" => (corpus, url, ai, flag),
    _ => null,
};

if (options is not { } parsed
    || !Uri.TryCreate(parsed.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
{
    Console.Error.WriteLine("Usage: PaymentOps.KnowledgePublish <corpus-directory> --endpoint <url> --ai <ai-directory> [--ensure-evaluation-index | --documents-only]");
    Console.Error.WriteLine("       PaymentOps.KnowledgePublish --self-test --endpoint <url> --ai <ai-directory>");
    return 2;
}

var (corpusDirectory, _, aiDirectory, mode) = parsed;
var documentsOnly = mode == "--documents-only";

// In the pipeline, azure/login has signed the CLI in as the deploy or evaluation-publish identity through OIDC.
var credential = new AzureCliCredential();

try
{
    var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(aiDirectory));
    var indexName = release.KnowledgeIndex;
    if (corpusDirectory is null)
    {
        foreach (var line in await KnowledgeSelfTest.RunAsync(SearchKnowledgeIndexClient.Create(endpoint, KnowledgeSelfTest.IndexName, credential), indexName))
        {
            Console.WriteLine($"Passed: {line}");
        }

        return 0;
    }

    KnowledgeSelfTest.RequireDistinctFrom(indexName);
    var documents = IndexedCorpus.Load(corpusDirectory);
    PublishPlan plan;
    if (documentsOnly)
    {
        // The live index's client is used for the probe only.
        await PublisherScope.RequireNoWriteAsync(SearchKnowledgeIndexClient.Create(endpoint, indexName, credential), indexName, CancellationToken.None);
        Console.WriteLine($"Scope check passed: writing '{indexName}' was refused (403).");
        indexName = release.EvaluationIndex;
        plan = await new KnowledgeIndexPublisher(SearchKnowledgeIndexClient.Create(endpoint, indexName, credential)).PublishDocumentsAsync(documents);
    }
    else
    {
        plan = await new KnowledgeIndexPublisher(SearchKnowledgeIndexClient.Create(endpoint, indexName, credential)).PublishAsync(documents);
    }

    foreach (var change in plan.Changes())
    {
        Console.WriteLine(change);
    }

    Console.WriteLine(
        $"Published {documents.Count} sections from {documents.Select(d => d.DocId).Distinct().Count()} documents to '{indexName}': "
        + $"{plan.Tighten.Count} tightened, {plan.Upload.Count} uploaded, {plan.Delete.Count} deleted. The index holds exactly the corpus.");

    if (mode == "--ensure-evaluation-index")
    {
        await SearchKnowledgeIndexClient.Create(endpoint, release.EvaluationIndex, credential).EnsureIndexAsync(CancellationToken.None);
        Console.WriteLine($"The evaluation index '{release.EvaluationIndex}' exists; evaluations publish its documents.");
    }

    return 0;
}
catch (KnowledgeCorpusException ex)
{
    GitHubAnnotation.Errors("Knowledge corpus", ex.Errors);
    return documentsOnly ? DocumentsOnlyExitCode.For(ex) : 1;
}
catch (AiReleaseException ex)
{
    GitHubAnnotation.Errors("AI release", ex.Errors);
    return documentsOnly ? DocumentsOnlyExitCode.For(ex) : 1;
}
catch (PublisherScopeException ex)
{
    GitHubAnnotation.Error("Publisher scope", ex.Message);
    return DocumentsOnlyExitCode.For(ex);
}
catch (KnowledgePublishException ex)
{
    GitHubAnnotation.Error("Knowledge publish", ex.Message);
    return documentsOnly ? DocumentsOnlyExitCode.For(ex) : 1;
}
catch (AuthenticationFailedException ex)
{
    GitHubAnnotation.Error("Knowledge publish", $"Couldn't get an Entra token from the Azure CLI: {ex.Message}");
    return documentsOnly ? DocumentsOnlyExitCode.For(ex) : 1;
}
catch (HttpRequestException ex) when (documentsOnly)
{
    GitHubAnnotation.Error("Knowledge publish", $"Search couldn't be reached: {ex.Message}");
    return DocumentsOnlyExitCode.For(ex);
}
