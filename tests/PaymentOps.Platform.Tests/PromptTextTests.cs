using PaymentOps.Platform.Ai;

namespace PaymentOps.Platform.Tests;

public sealed class PromptHasherTests
{
    [Fact]
    public void Matches_the_standard_sha256_test_vector()
    {
        Assert.Equal("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PromptHasher.Hash("abc"));
    }

    [Fact]
    public void Line_endings_do_not_change_the_hash()
    {
        var lf = PromptHasher.Hash("first\nsecond\n");

        Assert.Equal(lf, PromptHasher.Hash("first\r\nsecond\r\n"));
        Assert.Equal(lf, PromptHasher.Hash("first\rsecond\r"));
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\nsecond \n")]
    [InlineData("first\nsecond.\n")]
    [InlineData(" first\nsecond\n")]
    public void Any_other_difference_changes_the_hash(string variant)
    {
        Assert.NotEqual(PromptHasher.Hash("first\nsecond\n"), PromptHasher.Hash(variant));
    }

    [Fact]
    public void Unicode_is_not_normalized()
    {
        // "é" precomposed and as "e" plus a combining accent look the same but are different text.
        Assert.NotEqual(PromptHasher.Hash("café"), PromptHasher.Hash("café"));
    }
}

public sealed class PromptTemplateTests
{
    [Fact]
    public void Fills_placeholders_once_and_keeps_values_literal()
    {
        var template = Parse("Merchant {{merchantId}} on {{date}}.");

        var rendered = template.Render(new Dictionary<string, string> { ["merchantId"] = "{{date}}", ["date"] = "2026-10-01" });

        Assert.Equal("Merchant {{date}} on 2026-10-01.", rendered);
    }

    [Fact]
    public void A_lone_closing_pair_is_ordinary_text()
    {
        var template = Parse("""Reply as {"result": {"ok": true}}""");

        Assert.Empty(template.Variables);
        Assert.Equal("""Reply as {"result": {"ok": true}}""", template.Render(new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("Hello {{ name }}")]
    [InlineData("Hello {{name")]
    [InlineData("Hello {{1name}}")]
    [InlineData("Hello {{first.name}}")]
    [InlineData("Hello {{}}")]
    public void Malformed_placeholders_are_rejected(string text)
    {
        var errors = new List<string>();

        Assert.Null(PromptTemplate.Parse(text, "test.md", errors));
        Assert.Single(errors);
    }

    [Fact]
    public void Missing_and_unused_values_are_rejected()
    {
        var template = Parse("{{a}} and {{b}}");

        var ex = Assert.Throws<ArgumentException>(() => template.Render(new Dictionary<string, string> { ["a"] = "1", ["c"] = "3" }));

        Assert.Contains("Missing: [b]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unused: [c]", ex.Message, StringComparison.Ordinal);
    }

    private static PromptTemplate Parse(string text)
    {
        var errors = new List<string>();
        var template = PromptTemplate.Parse(text, "test.md", errors);
        Assert.Empty(errors);
        return template!;
    }
}
