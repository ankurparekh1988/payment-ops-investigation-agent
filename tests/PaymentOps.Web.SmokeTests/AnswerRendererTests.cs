using PaymentOps.Web.Rendering;

namespace PaymentOps.Web.SmokeTests;

public sealed class AnswerRendererTests
{
    [Fact]
    public void A_source_url_escapes_the_key() =>
        Assert.Equal("/sources/a%2Fb%3Fc", AnswerRenderer.SourceUrl("a/b?c"));

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("![x](https://evil.example/x.png)")]
    [InlineData("<https://evil.example>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Text <img src=x onerror=alert(1)> inline")]
    [InlineData("&lt;script&gt;")]
    [InlineData("[S1](https://evil.example)")]
    public void Nothing_in_a_section_becomes_markup_beyond_the_allowlist(string markdown)
    {
        var html = AnswerRenderer.RenderSection(markdown);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allowed_structure_is_rendered()
    {
        var html = AnswerRenderer.RenderSection("## Steps\n\n- **one**\n- *two*\n\n1. first\n\n`code`");

        Assert.Contains("<h3>Steps</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<ul><li><strong>one</strong></li><li><em>two</em></li></ul>", html, StringComparison.Ordinal);
        Assert.Contains("<ol><li>first</li></ol>", html, StringComparison.Ordinal);
        Assert.Contains("<code>code</code>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_encoded()
    {
        Assert.Equal("<p>a &lt; b &amp; c</p>", AnswerRenderer.RenderSection("a < b & c"));
    }

    [Fact]
    public void Section_content_never_links()
    {
        var html = AnswerRenderer.RenderSection("See [the guide](https://example.com) and [S1].");

        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
        Assert.Contains("the guide", html, StringComparison.Ordinal);
    }
}
