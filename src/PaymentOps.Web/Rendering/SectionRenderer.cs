using System.Text;
using System.Text.Encodings.Web;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using PaymentOps.Agent.Citations;

namespace PaymentOps.Web.Rendering;

/// <summary>
/// Turns corpus Markdown into HTML from an explicit allowlist. Text is always encoded, and there are
/// no links, images, raw HTML or targets: the corpus never chooses markup.
/// </summary>
internal static class SectionRenderer
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    public static string SourceUrl(string sectionKey) => $"/sources/{Uri.EscapeDataString(sectionKey)}";

    /// <summary>Corpus content for the source page.</summary>
    public static string RenderSection(string markdown)
    {
        var html = new StringBuilder();
        foreach (var block in Markdown.Parse(markdown, CitationScanner.Pipeline))
        {
            Block(html, block);
        }

        return html.ToString();
    }

    private static void Block(StringBuilder html, Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                // Demoted under the page's own heading.
                var level = Math.Min(heading.Level + 1, 6);
                html.Append("<h").Append(level).Append('>');
                Inlines(html, heading.Inline);
                html.Append("</h").Append(level).Append('>');
                break;
            case ParagraphBlock paragraph:
                html.Append("<p>");
                Inlines(html, paragraph.Inline);
                html.Append("</p>");
                break;
            case ListBlock list:
                var tag = list.IsOrdered ? "ol" : "ul";
                html.Append('<').Append(tag).Append('>');
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    html.Append("<li>");
                    foreach (var child in item)
                    {
                        // Tight lists show their item text without a paragraph around it.
                        if (child is ParagraphBlock paragraph && !list.IsLoose)
                        {
                            Inlines(html, paragraph.Inline);
                        }
                        else
                        {
                            Block(html, child);
                        }
                    }

                    html.Append("</li>");
                }

                html.Append("</").Append(tag).Append('>');
                break;
            case CodeBlock code:
                html.Append("<pre><code>").Append(Encoder.Encode(code.Lines.ToString())).Append("</code></pre>");
                break;
            case LeafBlock leaf:
                // Anything else (HTML blocks, thematic breaks…) is shown as its source text.
                html.Append("<p>").Append(Encoder.Encode(leaf.Lines.ToString())).Append("</p>");
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    Block(html, child);
                }

                break;
        }
    }

    private static void Inlines(StringBuilder html, ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        foreach (var segment in CitationScanner.Segments(container))
        {
            if (segment.Run is { } run)
            {
                html.Append(Encoder.Encode(run.Text));
                continue;
            }

            switch (segment.Other)
            {
                case EmphasisInline emphasis:
                    var tag = emphasis.DelimiterCount >= 2 ? "strong" : "em";
                    html.Append('<').Append(tag).Append('>');
                    Inlines(html, emphasis);
                    html.Append("</").Append(tag).Append('>');
                    break;
                case CodeInline code:
                    html.Append("<code>").Append(Encoder.Encode(code.Content)).Append("</code>");
                    break;
                case LineBreakInline:
                    html.Append("<br>");
                    break;
                case LinkInline link:
                    // Links and images keep their visible text only; the URL is dropped.
                    Inlines(html, link);
                    break;
                case AutolinkInline autolink:
                    html.Append(Encoder.Encode(autolink.Url));
                    break;
                case HtmlEntityInline entity:
                    html.Append(Encoder.Encode(entity.Original.ToString()));
                    break;
                case HtmlInline raw:
                    html.Append(Encoder.Encode(raw.Tag));
                    break;
                case ContainerInline nested:
                    Inlines(html, nested);
                    break;
                case { } other:
                    html.Append(Encoder.Encode(other.ToString() ?? string.Empty));
                    break;
            }
        }
    }
}
