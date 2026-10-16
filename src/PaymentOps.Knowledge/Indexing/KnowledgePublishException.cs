namespace PaymentOps.Knowledge.Indexing;

/// <summary>Publishing stopped. The message names sections and roles, never section text.</summary>
public sealed class KnowledgePublishException(string message, Exception? innerException = null)
    : Exception(message, innerException);
