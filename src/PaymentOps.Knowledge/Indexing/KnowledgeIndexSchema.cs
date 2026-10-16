using System.Text.Json;

using Azure.Search.Documents.Indexes.Models;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>The knowledge index's fields. A change Search can't apply in place needs a new index name.</summary>
internal static class KnowledgeIndexSchema
{
    /// <summary>How documents are written and read: field names are the properties in snake_case.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static SearchIndex Build(string indexName) => new(indexName)
    {
        Fields =
        {
            // Filterable and sortable so the read-back can page through every document by key range.
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
            new SimpleField("doc_id", SearchFieldDataType.String) { IsFilterable = true },
            new SearchableField("title"),
            new SimpleField("doc_type", SearchFieldDataType.String) { IsFilterable = true },

            // The security filter in Commit 10; values are "general" and "restricted".
            new SimpleField("classification", SearchFieldDataType.String) { IsFilterable = true },
            new SearchableField("h2"),
            new SearchableField("h3"),
            new SimpleField("section_path", SearchFieldDataType.String),
            new SimpleField("ordinal", SearchFieldDataType.Int32) { IsSortable = true },
            new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
        },
    };
}
