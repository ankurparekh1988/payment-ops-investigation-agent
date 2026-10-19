using Azure;
using Azure.Identity;

namespace PaymentOps.Knowledge.Retrieval;

/// <summary>Search couldn't answer. Carries what's safe to log for diagnosis; never query text.</summary>
public sealed class KnowledgeUnavailableException : Exception
{
    public KnowledgeUnavailableException()
    {
    }

    public KnowledgeUnavailableException(string message)
        : base(message)
    {
    }

    public KnowledgeUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    // Status 0 means no response at all: the service couldn't be reached.
    internal KnowledgeUnavailableException(RequestFailedException inner)
        : base(inner.Status == 0 ? $"Search couldn't be reached: {inner.Message}" : $"Search returned {inner.Status} ({inner.ErrorCode ?? "no error code"}).", inner)
    {
        Status = inner.Status == 0 ? null : inner.Status;
        ErrorCode = inner.ErrorCode;

        // Search's own id for the request, which is what its support asks for; ours only if it sent none.
        var response = inner.GetRawResponse();
        RequestId = response is not null && response.Headers.TryGetValue("request-id", out var searchId) ? searchId : response?.ClientRequestId;
    }

    // Every attempt failed without a response, so there is no status to report.
    private KnowledgeUnavailableException(string message, AggregateException retries)
        : base(message, retries)
    {
    }

    /// <summary>The SDK reports a request that failed on every retry as an aggregate; the last attempt is the one to report.</summary>
    internal static KnowledgeUnavailableException FromRetries(AggregateException retries) => retries.InnerExceptions[^1] switch
    {
        RequestFailedException failed => new KnowledgeUnavailableException(failed),
        AuthenticationFailedException token => new KnowledgeUnavailableException(token),
        _ => new KnowledgeUnavailableException("Search didn't respond on any attempt.", retries),
    };

    // The token request failed before anything reached Search, such as a managed-identity endpoint hiccup.
    internal KnowledgeUnavailableException(AuthenticationFailedException inner)
        : base($"Couldn't get a token for Search: {inner.Message}", inner)
    {
        IsAuthenticationFailure = true;
    }

    public bool IsAuthenticationFailure { get; }

    public int? Status { get; }

    public string? ErrorCode { get; }

    public string? RequestId { get; }
}
