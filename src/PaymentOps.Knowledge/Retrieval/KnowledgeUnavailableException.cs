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
        RequestId = inner.GetRawResponse()?.ClientRequestId;
    }

    // The SDK reports a request that failed on every retry as an aggregate; the last attempt is the one to report.
    internal KnowledgeUnavailableException(AggregateException retries)
        : this(retries.InnerExceptions.OfType<RequestFailedException>().Last())
    {
    }

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
