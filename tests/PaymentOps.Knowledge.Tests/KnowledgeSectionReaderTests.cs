using System.Text.Json;

using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeSectionReaderTests
{
    private const string Key = "risk-hold-review-policy--0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static (KnowledgeSectionReader Reader, StubSearchHandler Handler) Create(string json)
    {
        var handler = StubSearchHandler.Returning(json);
        return (KnowledgeSectionReader.Create(new Uri("https://search.example"), "knowledge-test", new StaticTokenCredential(), handler), handler);
    }

    [Fact]
    public async Task One_query_combines_the_key_and_the_access_predicate()
    {
        var (reader, handler) = Create(SearchResponses.Hits((Key, "general", null)));

        var section = await reader.FindAsync(Key, KnowledgeAccess.From(TestUser.General), CancellationToken.None);

        Assert.Equal(Key, section!.Key);
        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement;
        Assert.Equal($"id eq '{Key}' and classification eq 'general'", body.GetProperty("filter").GetString());
        Assert.Equal(1, body.GetProperty("top").GetInt32());
    }

    [Fact]
    public async Task No_hit_is_not_found_whatever_the_reason()
    {
        var (reader, _) = Create(SearchResponses.Hits());

        Assert.Null(await reader.FindAsync(Key, KnowledgeAccess.From(TestUser.General), CancellationToken.None));
    }

    [Fact]
    public async Task An_unreachable_service_is_reported_as_unavailable()
    {
        var handler = new StubSearchHandler(_ => throw new HttpRequestException("No such host is known."));
        var reader = KnowledgeSectionReader.Create(new Uri("https://search.example"), "knowledge-test", new StaticTokenCredential(), handler);

        await Assert.ThrowsAsync<KnowledgeUnavailableException>(() => reader.FindAsync(Key, KnowledgeAccess.From(TestUser.General), CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_token_request_is_reported_as_unavailable()
    {
        var reader = KnowledgeSectionReader.Create(new Uri("https://search.example"), "knowledge-test", new FailingTokenCredential(), StubSearchHandler.Returning(SearchResponses.Hits()));

        await Assert.ThrowsAsync<KnowledgeUnavailableException>(() => reader.FindAsync(Key, KnowledgeAccess.From(TestUser.General), CancellationToken.None));
    }

    [Theory]
    [InlineData("a'b--0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("doc--short")]
    [InlineData("")]
    [InlineData("doc--0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    public async Task A_malformed_key_is_never_queried(string key)
    {
        var (reader, handler) = Create(SearchResponses.Hits());

        Assert.Null(await reader.FindAsync(key, KnowledgeAccess.From(TestUser.Restricted), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}
