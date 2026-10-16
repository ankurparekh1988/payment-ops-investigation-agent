using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Tests;

/// <summary>Search documents built the way the corpus builds them, so keys follow the real rule.</summary>
internal static class TestSections
{
    public static KnowledgeSearchDocument Section(
        string docId = "runbook", string h2 = "", string h3 = "", string classification = KnowledgeClassification.General,
        string content = "Text.", string docType = "runbook")
    {
        var path = new SectionPath("Title of " + docId, h2, h3);
        return new KnowledgeSearchDocument(
            SectionKey.For(docId, path), docId, path.H1, docType, classification, h2, h3, path.ToString(), 0, content);
    }
}
