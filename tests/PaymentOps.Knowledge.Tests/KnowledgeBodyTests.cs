namespace PaymentOps.Knowledge.Tests;

public sealed class KnowledgeBodyTests
{
    [Theory]
    [InlineData("Intro text.\n\n# Missing settlement batch\n", "must start with a level-1 heading")]
    [InlineData("# Cut-offs\n", "'Cut-offs' must match the title 'Missing settlement batch'")]
    [InlineData("# Missing settlement batch\n\n# Another\n", "only one level-1 heading")]
    [InlineData("# Missing settlement batch\n\n### Too deep\n", "a level-3 heading needs a level-2 heading above it")]
    [InlineData("# Missing settlement batch\n\n## A\n\n#### Deeper\n", "headings go down to level 3")]
    [InlineData("# Missing settlement batch\n\n## See [this](https://example.com)\n", "headings must be plain text")]
    [InlineData("# Missing settlement batch\n\n## The `cutoff` field\n", "headings must be plain text")]
    [InlineData("# Missing settlement batch\n\n- item\n\n  ## In a list\n", "must not be inside lists")]
    [InlineData("# Missing settlement batch\n\n<div>hi</div>\n", "indexed as raw markup")]
    [InlineData("# Missing settlement batch\n\nSee <b>this</b>.\n", "indexed as raw markup")]
    [InlineData("Missing settlement batch\n========================\n", "underlined headings aren't allowed")]
    [InlineData("# Missing settlement batch\n\nSteps\n-----\n", "underlined headings aren't allowed")]
    [InlineData("# Missing settlement batch\n\n![chart](chart.png)\n", "images aren't allowed")]
    [InlineData("# Missing settlement batch\n\nSee [the runbook](other.md).\n", "must be an absolute https URL")]
    [InlineData("# Missing settlement batch\n\nSee <http://status.example.com>.\n", "must be an absolute https URL")]
    [InlineData("# Missing settlement batch\n\n## A\n\nx\n\n## A\n", "section 'Missing settlement batch > A' appears more than once")]
    public void Structure_rules(string body, string expected)
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: body));

        Assert.Contains(tree.Errors(), e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_heading_under_different_parents_is_fine()
    {
        const string Body = "# Missing settlement batch\n\n## Fabrikam Pay\n\n### Timing\n\n## Northwind Acquiring\n\n### Timing\n";
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: Body));

        Assert.Equal(5, tree.Read()[0].SectionPaths.Count);
    }

    [Fact]
    public void A_hash_inside_a_code_block_is_not_a_heading()
    {
        const string Body = "# Missing settlement batch\n\n```text\n# not a heading\n```\n\n    # nor this\n";
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: Body));

        Assert.Single(tree.Read()[0].SectionPaths);
    }

    [Fact]
    public void Errors_point_at_the_line_in_the_file()
    {
        // The front matter takes lines 1-5 and line 6 is blank, so the h1 is on line 7 and the h4 on line 9.
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: "# Missing settlement batch\n\n#### Deep\n"));

        Assert.Contains(tree.Errors(), e => e.StartsWith("general/missing-settlement-batch.md:9:", StringComparison.Ordinal));
    }

    [Fact]
    public void Errors_in_a_paragraph_point_at_its_line()
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: "# Missing settlement batch\n\nFirst.\n\nSee [the runbook](other.md).\n"));

        Assert.Contains(tree.Errors(), e => e.StartsWith("general/missing-settlement-batch.md:11:", StringComparison.Ordinal));
    }
}

public sealed class PublicContentCheckTests
{
    [Theory]
    [InlineData("Card 4242 4242 4242 4242 was used.")]
    [InlineData("Card 4242-4242-4242-4242 was used.")]
    [InlineData("Card 4000056655665556 was used.")]
    [InlineData("Card 4111 1111 1111 1111 12 28 was used.")]
    [InlineData("Ref 98 4111111111111111 was used.")]
    public void Numbers_that_pass_the_card_checksum_are_rejected(string text)
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: $"# Missing settlement batch\n\n{text}\n"));

        Assert.Contains(tree.Errors(), e => e.Contains("passes the card-number checksum", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Batch 4242 4242 4242 4241 closed.")]
    [InlineData("Card ending •••• 4242.")]
    [InlineData("On 2026-10-14 at 16:00, 1234 merchants were paid.")]
    public void Other_numbers_are_fine(string text)
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: $"# Missing settlement batch\n\n{text}\n"));

        Assert.Single(tree.Read());
    }

    [Theory]
    [InlineData("Write to ops@contoso.com.", "email 'ops@contoso.com'")]
    [InlineData("See https://status.fabrikam.com/now.", "URL 'https://status.fabrikam.com'")]
    [InlineData("See [status](https://status.fabrikam.com).", "URL 'https://status.fabrikam.com'")]
    [InlineData("```\ncurl http://internal.contoso.net/api\n```", "URL 'http://internal.contoso.net'")]
    public void Only_reserved_example_domains_are_allowed(string text, string expected)
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: $"# Missing settlement batch\n\n{text}\n"));

        Assert.Contains(tree.Errors(), e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Write to ops@example.com or desk@ops.example.org.")]
    [InlineData("See https://status.fabrikampay.example and https://example.net/x.")]
    [InlineData("See https://example.com, then **https://status.fabrikampay.example**.")]
    public void Example_domains_are_fine(string text)
    {
        using var tree = new SourceTree().Add("general/missing-settlement-batch.md", SourceTree.Source(body: $"# Missing settlement batch\n\n{text}\n"));

        Assert.Single(tree.Read());
    }
}
