using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Core.Serialization;
using Azure.Search.Documents;

using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Retrieval;

/// <summary>The query client for the knowledge index, signed in with an Entra token.</summary>
internal static class KnowledgeSearch
{
    /// <summary>The fields a query selects, so queries never depend on the indexing record's full shape.</summary>
    public static readonly string[] Fields = ["id", "doc_id", "title", "section_path", "content", "classification"];

    public static SearchClient Client(Uri endpoint, string indexName, TokenCredential credential, HttpMessageHandler? handler = null)
    {
        var options = new SearchClientOptions { Serializer = new JsonObjectSerializer(KnowledgeIndexSchema.Json) };
        if (handler is not null)
        {
            // Tests only: answer from a stub and fail fast instead of retrying.
            options.Transport = new HttpClientTransport(new HttpClient(handler));
            options.Retry.MaxRetries = 0;
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
}

/// <summary>A search hit with only the selected fields.</summary>
internal sealed record SearchHitDocument(string Id, string DocId, string Title, string SectionPath, string Content, string Classification)
{
    public RetrievedSection ToSection(double? rerankerScore) => new(Id, DocId, Title, SectionPath, Content, Classification, rerankerScore);
}
