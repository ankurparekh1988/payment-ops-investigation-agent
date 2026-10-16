using PaymentOps.Knowledge.Corpus;
using PaymentOps.Knowledge.Indexing;

using static PaymentOps.Knowledge.Tests.TestSections;

namespace PaymentOps.Knowledge.Tests;

public sealed class PublishPlanTests
{
    private static IReadOnlyList<string> Ids(IEnumerable<KnowledgeSearchDocument> documents) => [.. documents.Select(d => d.Id)];

    [Fact]
    public void Unchanged_needs_no_operations()
    {
        var documents = new[] { Section(h2: "A"), Section(h2: "B") };

        Assert.True(PublishPlan.Create(documents, documents).IsEmpty);
    }

    [Fact]
    public void A_renamed_heading_uploads_the_new_section_and_deletes_the_old()
    {
        var plan = PublishPlan.Create([Section(h2: "New")], [Section(h2: "Old")]);

        Assert.Equal(Ids([Section(h2: "New")]), Ids(plan.Upload));
        Assert.Equal(Ids([Section(h2: "Old")]), Ids(plan.Delete));
        Assert.Empty(plan.Tighten);
    }

    [Fact]
    public void General_to_restricted_tightens_then_uploads()
    {
        var plan = PublishPlan.Create([Section(classification: KnowledgeClassification.Restricted)], [Section()]);

        Assert.Equal(Ids([Section()]), Ids(plan.Tighten));
        Assert.Equal(Ids([Section()]), Ids(plan.Upload));
        Assert.Equal(KnowledgeClassification.Restricted, Assert.Single(plan.Upload).Classification);
        Assert.Empty(plan.Delete);
    }

    [Fact]
    public void Restricted_to_general_only_uploads()
    {
        var plan = PublishPlan.Create([Section()], [Section(classification: KnowledgeClassification.Restricted)]);

        Assert.Empty(plan.Tighten);
        Assert.Single(plan.Upload);
        Assert.Empty(plan.Delete);
    }

    [Fact]
    public void A_classification_only_change_is_found_by_comparing_fields()
    {
        var plan = PublishPlan.Create([Section(docType: "policy")], [Section(docType: "runbook")]);

        Assert.Single(plan.Upload);
    }

    [Fact]
    public void Any_stored_field_that_differs_is_uploaded_again()
    {
        var stored = Section() with { Content = "Edited in the portal." };

        Assert.Single(PublishPlan.Create([Section()], [stored]).Upload);
    }

    [Fact]
    public void A_removed_document_is_deleted()
    {
        var plan = PublishPlan.Create([Section("kept")], [Section("kept"), Section("removed", h2: "A"), Section("removed", h2: "B")]);

        Assert.Equal(["removed", "removed"], plan.Delete.Select(d => d.DocId));
        Assert.Empty(plan.Upload);
    }

    [Fact]
    public void A_restricted_document_with_a_renamed_heading_tightens_the_old_key_once()
    {
        var plan = PublishPlan.Create(
            [Section(h2: "New", classification: KnowledgeClassification.Restricted)],
            [Section(h2: "Old")]);

        Assert.Equal(Ids([Section(h2: "Old")]), Ids(plan.Tighten));
        Assert.Empty(plan.Delete);
        Assert.Equal(Ids([Section(h2: "New")]), Ids(plan.Upload));
    }

    [Fact]
    public void Changes_name_sections_without_their_text()
    {
        var plan = PublishPlan.Create([Section(h2: "New", content: "Secret text.")], [Section(h2: "Old")]);

        Assert.Equal(["upload   runbook: Title of runbook > New", "delete   runbook: Title of runbook > Old"], plan.Changes());
    }
}
