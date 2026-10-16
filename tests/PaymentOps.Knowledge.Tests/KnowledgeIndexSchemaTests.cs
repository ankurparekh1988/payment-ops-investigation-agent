using System.Text.Json;

using Azure.Search.Documents.Indexes.Models;

using PaymentOps.Knowledge.Indexing;

namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeIndexSchemaTests
{
    private static readonly SearchIndex Index = KnowledgeIndexSchema.Build("knowledge-v1");

    private static SearchField Field(string name) => Index.Fields.Single(f => f.Name == name);

    [Fact]
    public void Field_names_are_the_names_documents_are_written_with()
    {
        var written = JsonSerializer.SerializeToElement(TestSections.Section(), KnowledgeIndexSchema.Json)
            .EnumerateObject().Select(p => p.Name);

        Assert.Equal(written.Order(StringComparer.Ordinal), Index.Fields.Select(f => f.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_field_is_retrievable_so_the_publisher_can_compare_it()
    {
        Assert.All(Index.Fields, f => Assert.NotEqual(true, f.IsHidden));
    }

    [Fact]
    public void Fields_have_the_behaviour_commit_10_queries_rely_on()
    {
        Assert.True(Field("id") is { IsKey: true, IsFilterable: true, IsSortable: true });
        Assert.All(["doc_id", "doc_type", "classification"], name => Assert.True(Field(name).IsFilterable));
        Assert.All(["title", "h2", "h3", "content"], name => Assert.True(Field(name).IsSearchable));
        Assert.Equal(LexicalAnalyzerName.EnMicrosoft, Field("content").AnalyzerName);
        Assert.Equal(SearchFieldDataType.Int32, Field("ordinal").Type);
        Assert.True(Field("ordinal").IsSortable);
    }
}
