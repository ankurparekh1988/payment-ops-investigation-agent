using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Answering;

/// <summary>The release's mode and floor applied to every question; callers can't choose them.</summary>
internal sealed class ReleaseKnowledgeLookup(KnowledgeRetriever retriever, KnowledgeSectionReader reader, AiRelease release) : IKnowledgeLookup
{
    public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken) =>
        retriever.RetrieveAsync(question, access, release.RetrievalMode, release.SemanticFloor, cancellationToken);

    public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
        reader.FindAsync(sectionKey, access, cancellationToken);
}
