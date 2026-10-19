using System.Text;
using System.Text.Encodings.Web;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using PaymentOps.Agent.Citations;
using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Rendering;

/// <summary>
/// Turns model or corpus Markdown into HTML from an explicit allowlist. Text is always encoded, and
/// the only links are citation links the server builds from validated ids; the model never chooses
/// an href, an image, raw HTML or a target.
/// </summary>
internal static class AnswerRenderer
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;
    private static readonly IReadOnlyDictionary<string, string> NoCitations = new Dictionary<string, string>();

    public static string SourceUrl(string sectionKey) => $"/sources/{Uri.EscapeDataString(sectionKey)}";

    /// <summary>A validated answer: <c>[Sn]</c> markers that match its citations link to their sections.</summary>
    public static string RenderAnswer(string markdown, IReadOnlyList<Citation> citations)
    {
        ArgumentNullException.ThrowIfNull(citations);

        return Render(markdown, citations.ToDictionary(c => c.Id, c => c.SectionKey, StringComparer.Ordinal));
    }

    /// <summary>Corpus content for the source page: the same allowlist, and no links at all.</summary>
    public static string RenderSection(string markdown) => Render(markdown, NoCitations);

    private static string Render(string markdown, IReadOnlyDictionary<string, string> citationKeys)
    {
        var html = new StringBuilder();
        foreach (var block in Markdown.Parse(markdown, CitationScanner.Pipeline))
        {
            Block(html, block, citationKeys);
        }

        return html.ToString();
    }

    private static void Block(StringBuilder html, Block block, IReadOnlyDictionary<string, string> citationKeys)
    {
        switch (block)
        {
            case HeadingBlock heading:
                // Demoted under the page's own heading.
                var level = Math.Min(heading.Level + 1, 6);
                html.Append("<h").Append(level).Append('>');
                Inlines(html, heading.Inline, citationKeys);
                html.Append("</h").Append(level).Append('>');
                break;
            case ParagraphBlock paragraph:
                html.Append("<p>");
                Inlines(html, paragraph.Inline, citationKeys);
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
                            Inlines(html, paragraph.Inline, citationKeys);
                        }
                        else
                        {
                            Block(html, child, citationKeys);
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
                    Block(html, child, citationKeys);
                }

                break;
        }
    }

    private static void Inlines(StringBuilder html, ContainerInline? container, IReadOnlyDictionary<string, string> citationKeys)
    {
        if (container is null)
        {
            return;
        }

        foreach (var segment in CitationScanner.Segments(container))
        {
            if (segment.Run is { } run)
            {
                Text(html, run, citationKeys);
                continue;
            }

            switch (segment.Other)
            {
                case EmphasisInline emphasis:
                    var tag = emphasis.DelimiterCount >= 2 ? "strong" : "em";
                    html.Append('<').Append(tag).Append('>');
                    Inlines(html, emphasis, citationKeys);
                    html.Append("</").Append(tag).Append('>');
                    break;
                case CodeInline code:
                    html.Append("<code>").Append(Encoder.Encode(code.Content)).Append("</code>");
                    break;
                case LineBreakInline:
                    html.Append("<br>");
                    break;
                case LinkInline link:
                    // Links and images keep their visible text only, never as a citation; the URL is dropped.
                    Inlines(html, link, NoCitations);
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
                    Inlines(html, nested, citationKeys);
                    break;
                case { } other:
                    html.Append(Encoder.Encode(other.ToString() ?? string.Empty));
                    break;
            }
        }
    }

    private static void Text(StringBuilder html, TextRun run, IReadOnlyDictionary<string, string> citationKeys)
    {
        var position = 0;
        foreach (var token in CitationScanner.CitationsIn(run))
        {
            if (!citationKeys.TryGetValue(token.Id, out var key))
            {
                continue;
            }

            html.Append(Encoder.Encode(run.Text[position..token.Start]))
                .Append("<a href=\"").Append(Encoder.Encode(SourceUrl(key))).Append("\">")
                .Append(Encoder.Encode(run.Text.Substring(token.Start, token.Length))).Append("</a>");
            position = token.Start + token.Length;
        }

        html.Append(Encoder.Encode(run.Text[position..]));
    }
}
