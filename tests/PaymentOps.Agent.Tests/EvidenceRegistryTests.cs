using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Agent.Tests;

public sealed class EvidenceRegistryTests
{
    internal static RetrievedSection Section(string key, string title = "Title", string path = "Title > Section") =>
        new(key, "doc", title, path, "Text", "general", null);

    [Fact]
    public void Sections_get_turn_ids_in_retrieval_order()
    {
        var registry = EvidenceRegistry.From([Section("a"), Section("b")]);

        Assert.Equal(["S1", "S2"], registry.Entries.Select(e => e.Id));
        Assert.True(registry.Contains("S2"));
        Assert.False(registry.Contains("S3"));
    }

    [Fact]
    public void A_citation_carries_the_sections_metadata()
    {
        var registry = EvidenceRegistry.From([Section("a", "Missing settlement batch", "Missing settlement batch > Act")]);

        Assert.Equal(new Citation("S1", "a", "Missing settlement batch", "Missing settlement batch > Act"), registry.CitationFor("S1"));
    }
}
