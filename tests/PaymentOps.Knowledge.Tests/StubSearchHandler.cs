using System.Globalization;
using System.Net;
using System.Text;

using Azure.Core;

namespace PaymentOps.Knowledge.Tests;

/// <summary>Stands in for Search over HTTP: records each request and answers with canned JSON.</summary>
internal sealed class StubSearchHandler(Func<string, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
{
    public List<(Uri Uri, string Body)> Requests { get; } = [];

    public Dictionary<string, string> ResponseHeaders { get; init; } = [];

    public static StubSearchHandler Returning(string json) => new(_ => (HttpStatusCode.OK, json));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, body));
        var (status, json) = respond(body);
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in ResponseHeaders)
        {
            response.Headers.Add(name, value);
        }

        return response;
    }
}

/// <summary>A token that never leaves the test process. Search has key authentication disabled, so tests don't use keys either.</summary>
internal sealed class StaticTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("test-token", DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(GetToken(requestContext, cancellationToken));
}

internal static class SearchResponses
{
    public static string Hits(params (string Key, string Classification, double? Reranker)[] hits) =>
        "{\"value\":[" + string.Join(',', hits.Select(h =>
            "{\"@search.score\":1.0" + (h.Reranker is { } r ? ",\"@search.rerankerScore\":" + r.ToString(CultureInfo.InvariantCulture) : string.Empty) +
            $",\"id\":\"{h.Key}\",\"doc_id\":\"doc\",\"title\":\"Title\",\"section_path\":\"Title > Section\",\"content\":\"Text\",\"classification\":\"{h.Classification}\"}}")) + "]}";
}

/// <summary>A credential whose token request fails, as a managed-identity endpoint can transiently.</summary>
internal sealed class FailingTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new Azure.Identity.AuthenticationFailedException("The managed identity endpoint didn't respond.");

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new Azure.Identity.AuthenticationFailedException("The managed identity endpoint didn't respond.");
}
