using Azure;
using Azure.Core;
using Azure.Search.Documents;

using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Retrieval;

/// <summary>
/// Reads one section for the source view. Key and access are one query, so a section the user may
/// not see is indistinguishable from one that doesn't exist.
/// </summary>
public sealed class KnowledgeSectionReader
{
    private readonly SearchClient _client;

    private KnowledgeSectionReader(SearchClient client) => _client = client;

    public static KnowledgeSectionReader Create(Uri endpoint, string indexName, TokenCredential credential) =>
        new(KnowledgeSearch.Client(endpoint, indexName, credential));

    internal static KnowledgeSectionReader Create(Uri endpoint, string indexName, TokenCredential credential, HttpMessageHandler handler) =>
        new(KnowledgeSearch.Client(endpoint, indexName, credential, handler));

    public async Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        // The format check also guarantees the key has no quote to escape.
        if (sectionKey is null || !SectionKey.IsWellFormed(sectionKey))
        {
            return null;
        }

        try
        {
            var response = await _client.SearchAsync<SearchHitDocument>("*", KnowledgeSearch.Options(1, $"id eq '{sectionKey}' and {access.Predicate}"), cancellationToken);
            await foreach (var hit in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
            {
                return hit.Document.ToSection(null);
            }

            return null;
        }
        catch (RequestFailedException ex)
        {
            throw new KnowledgeUnavailableException(ex);
        }
    }
}
