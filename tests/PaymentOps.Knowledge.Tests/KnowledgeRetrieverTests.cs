using System.Net;
using System.Text.Json;

using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeRetrieverTests
{
    private static (KnowledgeRetriever Retriever, StubSearchHandler Handler) Create(string json)
    {
        var handler = StubSearchHandler.Returning(json);
        return (KnowledgeRetriever.Create(new Uri("https://search.example"), "knowledge-test", new StaticTokenCredential(), handler), handler);
    }

    private static JsonElement Request(StubSearchHandler handler) => JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement;

    [Fact]
    public async Task A_lexical_query_asks_for_five_authorized_hits_without_semantic_settings()
    {
        var (retriever, handler) = Create(SearchResponses.Hits());

        await retriever.RetrieveAsync("late batch", KnowledgeAccess.From(TestUser.General), RetrievalMode.Lexical, null, CancellationToken.None);

        var body = Request(handler);
        Assert.Equal(5, body.GetProperty("top").GetInt32());
        Assert.Equal("classification eq 'general'", body.GetProperty("filter").GetString());
        Assert.False(body.TryGetProperty("queryType", out _));
        Assert.False(body.TryGetProperty("semanticConfiguration", out _));
    }

    [Fact]
    public async Task A_semantic_query_uses_the_same_filter_and_the_semantic_configuration()
    {
        var (retriever, handler) = Create(SearchResponses.Hits());

        await retriever.RetrieveAsync("late batch", KnowledgeAccess.From(TestUser.General), RetrievalMode.Semantic, null, CancellationToken.None);

        var body = Request(handler);
        Assert.Equal(5, body.GetProperty("top").GetInt32());
        Assert.Equal("classification eq 'general'", body.GetProperty("filter").GetString());
        Assert.Equal("semantic", body.GetProperty("queryType").GetString());
        Assert.Equal("knowledge-semantic", body.GetProperty("semanticConfiguration").GetString());
    }

    [Fact]
    public async Task General_and_restricted_requests_differ_only_in_the_classification_predicate()
    {
        var (general, generalHandler) = Create(SearchResponses.Hits());
        var (restricted, restrictedHandler) = Create(SearchResponses.Hits());

        await general.RetrieveAsync("q", KnowledgeAccess.From(TestUser.General), RetrievalMode.Lexical, null, CancellationToken.None);
        await restricted.RetrieveAsync("q", KnowledgeAccess.From(TestUser.Restricted), RetrievalMode.Lexical, null, CancellationToken.None);

        var a = Request(generalHandler).EnumerateObject().Where(p => p.Name != "filter").Select(p => p.ToString());
        var b = Request(restrictedHandler).EnumerateObject().Where(p => p.Name != "filter").Select(p => p.ToString());
        Assert.Equal(a, b);
        Assert.NotEqual(Request(generalHandler).GetProperty("filter").GetString(), Request(restrictedHandler).GetProperty("filter").GetString());
    }

    [Fact]
    public async Task Question_text_never_reaches_the_filter()
    {
        var (retriever, handler) = Create(SearchResponses.Hits());

        await retriever.RetrieveAsync("x' or classification eq 'restricted", KnowledgeAccess.From(TestUser.General), RetrievalMode.Lexical, null, CancellationToken.None);

        Assert.Equal("classification eq 'general'", Request(handler).GetProperty("filter").GetString());
    }

    [Fact]
    public async Task Hits_come_back_as_sections_in_search_order()
    {
        var (retriever, _) = Create(SearchResponses.Hits(("b", "general", null), ("a", "restricted", null)));

        var hits = await retriever.RetrieveAsync("q", KnowledgeAccess.From(TestUser.Restricted), RetrievalMode.Lexical, null, CancellationToken.None);

        Assert.Equal(["b", "a"], hits.Select(h => h.Key));
        Assert.Equal("restricted", hits[1].Classification);
        Assert.Equal("Title > Section", hits[0].SectionPath);
        Assert.Null(hits[0].RerankerScore);
    }

    [Fact]
    public async Task The_floor_drops_hits_after_search_returns_them()
    {
        var (retriever, handler) = Create(SearchResponses.Hits(("a", "general", 3.25), ("b", "general", 2.5), ("c", "general", 1.75)));

        var hits = await retriever.RetrieveAsync("q", KnowledgeAccess.From(TestUser.General), RetrievalMode.Semantic, 2.0, CancellationToken.None);

        Assert.Equal(["a", "b"], hits.Select(h => h.Key));
        Assert.Equal([3.25, 2.5], hits.Select(h => h.RerankerScore!.Value));
        Assert.Equal("classification eq 'general'", Request(handler).GetProperty("filter").GetString());
    }

    [Fact]
    public async Task A_search_failure_is_reported_as_unavailable_with_its_status()
    {
        var handler = new StubSearchHandler(_ => (HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\",\"message\":\"no\"}}"));
        var retriever = KnowledgeRetriever.Create(new Uri("https://search.example"), "knowledge-test", new StaticTokenCredential(), handler);

        var ex = await Assert.ThrowsAsync<KnowledgeUnavailableException>(() =>
            retriever.RetrieveAsync("q", KnowledgeAccess.From(TestUser.General), RetrievalMode.Lexical, null, CancellationToken.None));

        Assert.Equal(403, ex.Status);
    }

    [Fact]
    public async Task An_unreachable_service_is_reported_as_unavailable_after_retries()
    {
        var handler = new StubSearchHandler(_ => throw new HttpRequestException("No such host is known."));
        var retriever = KnowledgeRetriever.Create(new Uri("https://search.example"), "knowledge-test", new StaticTokenCredential(), handler);

        var ex = await Assert.ThrowsAsync<KnowledgeUnavailableException>(() =>
            retriever.RetrieveAsync("q", KnowledgeAccess.From(TestUser.General), RetrievalMode.Lexical, null, CancellationToken.None));
        Assert.True(handler.Requests.Count > 1);
        Assert.Null(ex.Status);
        Assert.Contains("couldn't be reached", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_service_fails_the_readiness_probe_as_unavailable()
    {
        var handler = new StubSearchHandler(_ => throw new HttpRequestException("No such host is known."));
        var retriever = KnowledgeRetriever.Create(new Uri("https://search.example"), "knowledge-test", new StaticTokenCredential(), handler);

        await Assert.ThrowsAsync<KnowledgeUnavailableException>(() => retriever.CheckReachableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var (retriever, _) = Create(SearchResponses.Hits());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            retriever.RetrieveAsync("q", KnowledgeAccess.From(TestUser.General), RetrievalMode.Lexical, null, cancelled.Token));
    }

    [Fact]
    public async Task The_readiness_probe_asks_for_no_documents()
    {
        var (retriever, handler) = Create(SearchResponses.Hits());

        await retriever.CheckReachableAsync(CancellationToken.None);

        Assert.Equal(0, Request(handler).GetProperty("top").GetInt32());
    }
}
