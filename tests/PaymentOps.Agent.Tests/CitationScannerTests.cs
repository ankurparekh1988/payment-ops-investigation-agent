using PaymentOps.Agent.Citations;

namespace PaymentOps.Agent.Tests;

public sealed class CitationScannerTests
{
    [Theory]
    [InlineData("Late batches roll forward [S1].", new[] { "S1" })]
    [InlineData("A [S1] and B [S2], again [S1].", new[] { "S1", "S2", "S1" })]
    [InlineData("- item [S3]\n- **bold [S4]**", new[] { "S3", "S4" })]
    [InlineData("Two in a row [S1][S2].", new[] { "S1", "S2" })]
    public void Ordinary_text_markers_are_citations(string markdown, string[] expected)
    {
        Assert.Equal(expected, CitationScanner.Scan(markdown).Citations.Select(c => c.Id));
    }

    [Theory]
    [InlineData("See [S1](https://evil.example).")]
    [InlineData("See `[S1]`.")]
    [InlineData("```\n[S1]\n```")]
    [InlineData("See &#91;S1&#93;.")]
    [InlineData("<div>\n[S1]\n</div>")]
    [InlineData("![S1](https://evil.example/x.png)")]
    [InlineData("<https://evil.example/[S1]>")]
    public void Markers_outside_ordinary_text_are_not_citations(string markdown)
    {
        Assert.Empty(CitationScanner.Scan(markdown).Citations);
    }

    [Theory]
    [InlineData("Text [s1].")]
    [InlineData("Text [S01].")]
    [InlineData("Text [S1, S2].")]
    [InlineData("Text [S1 ].")]
    [InlineData("Text [S0].")]
    public void Near_miss_markers_are_malformed(string markdown)
    {
        Assert.True(CitationScanner.Scan(markdown).HasMalformedMarker);
    }

    [Fact]
    public void Plain_brackets_are_not_malformed_markers()
    {
        Assert.False(CitationScanner.Scan("Use the [settlement calendar] and [Step 2].").HasMalformedMarker);
    }
}
