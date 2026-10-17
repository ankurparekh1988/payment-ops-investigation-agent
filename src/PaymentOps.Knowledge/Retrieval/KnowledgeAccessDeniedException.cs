namespace PaymentOps.Knowledge.Retrieval;

/// <summary>The user isn't signed in, so no knowledge may be retrieved for them.</summary>
public sealed class KnowledgeAccessDeniedException : Exception
{
    public KnowledgeAccessDeniedException()
        : base("Knowledge access requires a signed-in user.")
    {
    }

    public KnowledgeAccessDeniedException(string message)
        : base(message)
    {
    }

    public KnowledgeAccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
