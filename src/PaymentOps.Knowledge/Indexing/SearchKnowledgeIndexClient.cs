using System.Globalization;
using System.Text.Json;

using Azure;
using Azure.Core;
using Azure.Core.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// The knowledge index in Azure AI Search, reached with an Entra token. Local authentication is
/// disabled on the service, so there is no key to pass even if one were wanted.
/// </summary>
public sealed class SearchKnowledgeIndexClient : IKnowledgeIndexClient
{
    private const string ServiceRole = "Search Service Contributor";
    private const string DataRole = "Search Index Data Contributor";

    // Search takes up to 1,000 documents or about 16 MB per indexing request, whichever comes first;
    // the byte limit leaves room for the request envelope.
    internal const int MaxBatchCount = 1000;
    internal const long MaxBatchBytes = 12 * 1024 * 1024;

    private readonly SearchIndexClient _indexes;
    private readonly SearchClient _documents;
    private readonly string _indexName;

    private SearchKnowledgeIndexClient(SearchIndexClient indexes, string indexName)
    {
        _indexes = indexes;
        _indexName = indexName;
        _documents = indexes.GetSearchClient(indexName);
    }

    public static SearchKnowledgeIndexClient Create(Uri endpoint, string indexName, TokenCredential credential) =>
        new(new SearchIndexClient(endpoint, credential, new SearchClientOptions { Serializer = new JsonObjectSerializer(KnowledgeIndexSchema.Json) }), indexName);

    public async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _indexes.CreateOrUpdateIndexAsync(KnowledgeIndexSchema.Build(_indexName), cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw Explain(ex, $"Creating or updating index '{_indexName}'", ServiceRole,
                schemaHint: "the index exists with a schema Search can't change in place; change retrieval.index in ai/manifest.yaml to build a new one");
        }
    }

    public async Task DeleteIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _indexes.DeleteIndexAsync(_indexName, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
        catch (RequestFailedException ex)
        {
            throw Explain(ex, $"Deleting index '{_indexName}'", ServiceRole);
        }
    }

    public async Task<IReadOnlyList<KnowledgeSearchDocument>> ReadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAllPagesAsync(ReadPageAfterAsync, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw Explain(ex, $"Reading index '{_indexName}'", DataRole);
        }
    }

    private async Task<IReadOnlyList<KnowledgeSearchDocument>> ReadPageAfterAsync(string? lastKey, CancellationToken cancellationToken)
    {
        var options = new SearchOptions
        {
            Size = MaxBatchCount,
            OrderBy = { "id asc" },
            Filter = lastKey is null ? null : SearchFilter.Create($"id gt {lastKey}", CultureInfo.InvariantCulture),
        };

        var response = await _documents.SearchAsync<KnowledgeSearchDocument>("*", options, cancellationToken);
        var page = new List<KnowledgeSearchDocument>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            page.Add(Normalize(result.Document));
        }

        return page;
    }

    // Pages by key range rather than one capped query or $skip, so no stored document can be missed
    // however many there are.
    internal static async Task<List<KnowledgeSearchDocument>> ReadAllPagesAsync(
        Func<string?, CancellationToken, Task<IReadOnlyList<KnowledgeSearchDocument>>> readPageAfter, CancellationToken cancellationToken)
    {
        var documents = new List<KnowledgeSearchDocument>();
        while (true)
        {
            var page = await readPageAfter(documents.Count == 0 ? null : documents[^1].Id, cancellationToken);
            documents.AddRange(page);
            if (page.Count < MaxBatchCount)
            {
                return documents;
            }
        }
    }

    public async Task<IReadOnlyList<string>> FindExistingAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        foreach (var key in keys)
        {
            try
            {
                await _documents.GetDocumentAsync<KnowledgeSearchDocument>(key, cancellationToken: cancellationToken);
                found.Add(key);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }
            catch (RequestFailedException ex)
            {
                throw Explain(ex, $"Looking up a section in '{_indexName}'", DataRole);
            }
        }

        return found;
    }

    // Batched up front, so a section too large to send is refused before any batch is uploaded.
    public Task UploadAsync(IReadOnlyCollection<KnowledgeSearchDocument> documents, CancellationToken cancellationToken) =>
        IndexAsync(UploadBatches(documents).ToList().Select(IndexDocumentsBatch.Upload), "Uploading sections", cancellationToken);

    // A thousand keys are far below the request size limit, so deletes batch by count alone.
    public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        IndexAsync(keys.Chunk(MaxBatchCount).Select(chunk => IndexDocumentsBatch.Delete("id", chunk)), "Deleting sections", cancellationToken);

    internal static IEnumerable<KnowledgeSearchDocument[]> UploadBatches(
        IEnumerable<KnowledgeSearchDocument> documents, int maxCount = MaxBatchCount, long maxBytes = MaxBatchBytes)
    {
        var batch = new List<KnowledgeSearchDocument>();
        long batchBytes = 0;
        foreach (var document in documents)
        {
            long size = JsonSerializer.SerializeToUtf8Bytes(document, KnowledgeIndexSchema.Json).Length;
            if (size > maxBytes)
            {
                throw new KnowledgePublishException($"Section '{document.Describe()}' is {size / 1024} KB as JSON, more than one indexing request can carry.");
            }

            if (batch.Count == maxCount || batchBytes + size > maxBytes)
            {
                yield return [.. batch];
                batch.Clear();
                batchBytes = 0;
            }

            batch.Add(document);
            batchBytes += size;
        }

        if (batch.Count > 0)
        {
            yield return [.. batch];
        }
    }

    // Search reports a rejected document inside a successful response, so each result is checked.
    private async Task IndexAsync<T>(IEnumerable<IndexDocumentsBatch<T>> batches, string operation, CancellationToken cancellationToken)
    {
        foreach (var batch in batches)
        {
            IndexDocumentsResult result;
            try
            {
                result = (await _documents.IndexDocumentsAsync(batch, cancellationToken: cancellationToken)).Value;
            }
            catch (RequestFailedException ex)
            {
                throw Explain(ex, operation, DataRole);
            }

            var failed = result.Results.Where(r => !r.Succeeded).ToList();
            if (failed.Count > 0)
            {
                throw new KnowledgePublishException($"{operation} was rejected for {failed.Count} sections: {string.Join("; ", failed.Select(r => $"{r.Key} ({r.Status})"))}.");
            }
        }
    }

    // 401 is the token (missing, expired, wrong audience); 403 is a valid token without the role.
    internal static KnowledgePublishException Explain(RequestFailedException ex, string operation, string role, string? schemaHint = null) =>
        ex.Status switch
        {
            401 => new($"{operation} wasn't authenticated (401): Search didn't accept the Entra token. Check the Azure sign-in step and that the token is for Search.", ex),
            403 => new($"{operation} was forbidden (403). The deploy identity needs {role}: re-run scripts/bootstrap.sh.", ex),
            400 when schemaHint is not null && ex.ErrorCode == "OperationNotAllowed" => new($"{operation} failed ({ex.Status} {ex.ErrorCode}): {schemaHint}.", ex),
            _ => new($"{operation} failed ({ex.Status} {ex.ErrorCode}).", ex),
        };

    // Search may return an empty string field as null; the corpus stores "" for a level the path doesn't reach.
    internal static KnowledgeSearchDocument Normalize(KnowledgeSearchDocument document) =>
        document with { H2 = document.H2 ?? string.Empty, H3 = document.H3 ?? string.Empty };
}
