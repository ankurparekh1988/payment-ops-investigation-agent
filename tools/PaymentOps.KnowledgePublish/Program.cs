// Publishes a corpus build to the knowledge index the AI release names, then verifies that the index
// holds exactly that corpus. --self-test instead proves the publishing behaviours on a throwaway
// index. Signs in with the Azure CLI session; Search has key authentication disabled.
//
// Usage: PaymentOps.KnowledgePublish <corpus-directory> --endpoint <url> --ai <ai-directory>
//        PaymentOps.KnowledgePublish --self-test --endpoint <url> --ai <ai-directory>

using Azure.Identity;

using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

(string? Corpus, string Endpoint, string Ai)? options = args switch
{
    ["--self-test", "--endpoint", var url, "--ai", var ai] => (null, url, ai),
    [var corpus, "--endpoint", var url, "--ai", var ai] => (corpus, url, ai),
    _ => null,
};

if (options is not { } parsed
    || !Uri.TryCreate(parsed.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
{
    Console.Error.WriteLine("Usage: PaymentOps.KnowledgePublish <corpus-directory> --endpoint <url> --ai <ai-directory>");
    Console.Error.WriteLine("       PaymentOps.KnowledgePublish --self-test --endpoint <url> --ai <ai-directory>");
    return 2;
}

var (corpusDirectory, _, aiDirectory) = parsed;

// In the pipeline, azure/login has signed the CLI in as the deploy identity through OIDC.
var credential = new AzureCliCredential();

try
{
    var indexName = AiReleaseLoader.Load(new DirectoryAiReleaseSource(aiDirectory)).KnowledgeIndex;
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
    var publisher = new KnowledgeIndexPublisher(SearchKnowledgeIndexClient.Create(endpoint, indexName, credential));
    var plan = await publisher.PublishAsync(documents);

    foreach (var change in plan.Changes())
    {
        Console.WriteLine(change);
    }

    Console.WriteLine(
        $"Published {documents.Count} sections from {documents.Select(d => d.DocId).Distinct().Count()} documents to '{indexName}': "
        + $"{plan.Tighten.Count} tightened, {plan.Upload.Count} uploaded, {plan.Delete.Count} deleted. The index holds exactly the corpus.");
    return 0;
}
catch (KnowledgeCorpusException ex)
{
    GitHubAnnotation.Errors("Knowledge corpus", ex.Errors);

    return 1;
}
catch (AiReleaseException ex)
{
    GitHubAnnotation.Errors("AI release", ex.Errors);

    return 1;
}
catch (KnowledgePublishException ex)
{
    GitHubAnnotation.Error("Knowledge publish", ex.Message);
    return 1;
}
catch (AuthenticationFailedException ex)
{
    GitHubAnnotation.Error("Knowledge publish", $"Couldn't get an Entra token from the Azure CLI: {ex.Message}");
    return 1;
}
