using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Search.Documents;

using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Retrieval;

/// <summary>The query client for the knowledge index, signed in with an Entra token.</summary>
internal static class KnowledgeSearch
{
    /// <summary>The fields a query selects, so queries never depend on the indexing record's full shape.</summary>
    private static readonly string[] Fields = ["id", "doc_id", "title", "section_path", "content", "classification"];

    public static SearchClient Client(Uri endpoint, string indexName, TokenCredential credential, HttpMessageHandler? handler = null)
    {
        var options = KnowledgeIndexSchema.ClientOptions();
        if (handler is not null)
        {
            // Tests only: answer from a stub, with one quick retry so retry behaviour stays exercised.
            options.Transport = new HttpClientTransport(new HttpClient(handler));
            options.Retry.MaxRetries = 1;
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
        }

        return new SearchClient(endpoint, indexName, credential, options);
    }

    public static SearchOptions Options(int size, string filter)
    {
        var options = new SearchOptions { Size = size, Filter = filter };
        foreach (var field in Fields)
        {
            options.Select.Add(field);
        }

        return options;
    }
    /// <summary>Runs a Search call, reporting every way Search can fail to answer as one exception.</summary>
    public static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RequestFailedException ex)
        {
            throw new KnowledgeUnavailableException(ex);
        }

        // A request that failed on every retry, whichever way each attempt failed.
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is RequestFailedException or OperationCanceledException or AuthenticationFailedException or IOException))
        {
            throw KnowledgeUnavailableException.FromRetries(ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new KnowledgeUnavailableException(ex);
        }
    }
}

/// <summary>A search hit with only the selected fields.</summary>
internal sealed record SearchHitDocument(string Id, string DocId, string Title, string SectionPath, string Content, string Classification)
{
    public RetrievedSection ToSection(double? rerankerScore) => new(Id, DocId, Title, SectionPath, Content, Classification, rerankerScore);

}
