using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Tools.Knowledge;

/// <summary>Knowledge as the answering and the tools see it: retrieval mode and floor are already decided.</summary>
public interface IKnowledgeLookup
{
    Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken);

    Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken);
}
