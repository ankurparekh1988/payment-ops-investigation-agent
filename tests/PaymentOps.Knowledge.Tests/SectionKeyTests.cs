using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Tests;

public sealed class SectionKeyTests
{
    private static readonly SectionPath Path = new("Missing settlement batch", "Confirm", "Partial files");

    [Fact]
    public void Same_path_same_key()
    {
        Assert.Equal(SectionKey.For("missing-settlement-batch", Path), SectionKey.For("missing-settlement-batch", Path with { }));
    }

    [Fact]
    public void A_renamed_heading_changes_the_key()
    {
        Assert.NotEqual(SectionKey.For("doc", Path), SectionKey.For("doc", Path with { H3 = "Split files" }));
    }

    [Theory]
    [InlineData("a > b", "", "", "a", "b", "")]
    [InlineData("ab", "", "", "a", "b", "")]
    [InlineData("a", "", "b", "a", "b", "")]
    public void Paths_that_join_to_the_same_text_get_different_keys(string a1, string a2, string a3, string b1, string b2, string b3)
    {
        Assert.NotEqual(SectionKey.For("doc", new(a1, a2, a3)), SectionKey.For("doc", new(b1, b2, b3)));
    }

    [Fact]
    public void The_key_is_doc_id_and_a_full_hash_within_search_limits()
    {
        var docId = new string('a', 64);

        var key = SectionKey.For(docId, Path);

        Assert.Matches("^a{64}--[0-9a-f]{64}$", key);
        Assert.Equal(130, key.Length);
    }
}
