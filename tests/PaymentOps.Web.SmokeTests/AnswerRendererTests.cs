using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Rendering;

namespace PaymentOps.Web.SmokeTests;

public sealed class AnswerRendererTests
{
    private static readonly string Key = "doc-a--" + new string('a', 64);
    private static readonly Citation[] Citations = [new("S1", Key, "Doc A", "Doc A > One")];

    [Fact]
    public void A_citation_becomes_a_server_link()
    {
        var html = AnswerRenderer.RenderAnswer("Late files roll forward [S1].", Citations);

        Assert.Equal($"<p>Late files roll forward <a href=\"/sources/{Key}\">[S1]</a>.</p>", html);
    }

    [Fact]
    public void Adjacent_citations_each_become_links()
    {
        Citation[] two = [.. Citations, new("S2", "doc-b--" + new string('b', 64), "Doc B", "Doc B > Two")];

        var html = AnswerRenderer.RenderAnswer("Both [S1][S2].", two);

        Assert.Contains($"<a href=\"/sources/{Key}\">[S1]</a><a href=\"/sources/doc-b--{new string('b', 64)}\">[S2]</a>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("![x](https://evil.example/x.png)")]
    [InlineData("<https://evil.example>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Text <img src=x onerror=alert(1)> inline")]
    [InlineData("&lt;script&gt;")]
    [InlineData("[S1](https://evil.example)")]
    public void Nothing_the_model_writes_becomes_markup_beyond_the_allowlist(string markdown)
    {
        var html = AnswerRenderer.RenderAnswer(markdown, Citations);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"https", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("See `[S1]`.")]
    [InlineData("```\n[S1]\n```")]
    [InlineData("[S1](https://evil.example)")]
    public void Citation_lookalikes_in_code_or_links_stay_text(string markdown)
    {
        Assert.DoesNotContain("/sources/", AnswerRenderer.RenderAnswer(markdown, Citations), StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_without_a_citation_stays_text()
    {
        Assert.DoesNotContain("<a ", AnswerRenderer.RenderAnswer("Unknown [S9].", Citations), StringComparison.Ordinal);
    }

    [Fact]
    public void Allowed_structure_is_rendered()
    {
        var html = AnswerRenderer.RenderAnswer("## Steps\n\n- **one**\n- *two*\n\n1. first\n\n`code`", Citations);

        Assert.Contains("<h3>Steps</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<ul><li><strong>one</strong></li><li><em>two</em></li></ul>", html, StringComparison.Ordinal);
        Assert.Contains("<ol><li>first</li></ol>", html, StringComparison.Ordinal);
        Assert.Contains("<code>code</code>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_encoded()
    {
        Assert.Equal("<p>a &lt; b &amp; c</p>", AnswerRenderer.RenderAnswer("a < b & c", Citations));
    }

    [Fact]
    public void Section_content_never_links()
    {
        var html = AnswerRenderer.RenderSection("See [the guide](https://example.com) and [S1].");

        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
        Assert.Contains("the guide", html, StringComparison.Ordinal);
    }
}
