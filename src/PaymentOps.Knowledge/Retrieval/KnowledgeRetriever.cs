using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Knowledge.Retrieval;

/// <summary>
/// Finds the sections a user may see for a question. The app and the retrieval calibration both
/// call this, so they measure and serve the same query.
/// </summary>
public sealed class KnowledgeRetriever
{
    public const int Top = 5;

    private readonly SearchClient _client;

    private KnowledgeRetriever(SearchClient client) => _client = client;

    public static KnowledgeRetriever Create(Uri endpoint, string indexName, TokenCredential credential) =>
        new(KnowledgeSearch.Client(endpoint, indexName, credential));

    internal static KnowledgeRetriever Create(Uri endpoint, string indexName, TokenCredential credential, HttpMessageHandler handler) =>
        new(KnowledgeSearch.Client(endpoint, indexName, credential, handler));

    public async Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(
        string question, KnowledgeAccess access, RetrievalMode mode, double? semanticFloor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var options = KnowledgeSearch.Options(Top, access.Predicate);
        if (mode == RetrievalMode.Semantic)
        {
            options.QueryType = SearchQueryType.Semantic;
            // Fail rather than fall back: Search otherwise returns plain lexical results, unlabelled, when semantic ranking can't run.
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = KnowledgeIndexSchema.SemanticConfigurationName,
                ErrorMode = SemanticErrorMode.Fail,
            };
        }

        return await KnowledgeSearch.CallAsync<IReadOnlyList<RetrievedSection>>(async () =>
        {
            var response = await _client.SearchAsync<SearchHitDocument>(question, options, cancellationToken);
            var sections = new List<RetrievedSection>();
            await foreach (var hit in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
            {
                var score = hit.SemanticSearch?.RerankerScore;

                // The floor is our evidence gate on the returned scores, not a Search filter.
                if (semanticFloor is { } floor && (score ?? 0) < floor)
                {
                    continue;
                }

                sections.Add(hit.Document.ToSection(score));
            }

            return sections;
        });
    }

    /// <summary>Readiness: one query that returns no documents, proving endpoint, index, token and role.</summary>
    public async Task CheckReachableAsync(CancellationToken cancellationToken)
    {
        await KnowledgeSearch.CallAsync(() => _client.SearchAsync<SearchHitDocument>("*", new SearchOptions { Size = 0 }, cancellationToken));
    }
}
