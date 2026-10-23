using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Tests;

/// <summary>The real corpus: a query naming a document returns its sections, filtered by the caller's access.</summary>
internal sealed class CorpusKnowledge : IKnowledgeLookup
{
    private static readonly Lazy<IReadOnlyList<KnowledgeSearchDocument>> Corpus =
        new(() => IndexedCorpus.FromSourceDirectory(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src")));

    public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RetrievedSection>>([.. Corpus.Value
            .Where(d => d.DocId == question && (access.IncludesRestricted || d.Classification == KnowledgeClassification.General))
            .Take(3)
            .Select(d => new RetrievedSection(d.Id, d.DocId, d.Title, d.SectionPath, d.Content, d.Classification, null))]);

    public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
        Task.FromResult<RetrievedSection?>(null);
}
