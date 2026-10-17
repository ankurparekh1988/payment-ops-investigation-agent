using Azure;

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

    internal KnowledgeUnavailableException(RequestFailedException inner)
        : base($"Search returned {inner.Status} ({inner.ErrorCode ?? "no error code"}).", inner)
    {
        Status = inner.Status;
        ErrorCode = inner.ErrorCode;
        RequestId = inner.GetRawResponse()?.ClientRequestId;
    }

    public int? Status { get; }

    public string? ErrorCode { get; }

    public string? RequestId { get; }
}
