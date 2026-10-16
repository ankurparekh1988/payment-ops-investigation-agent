using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PaymentOps.Knowledge.Corpus;

/// <summary>
/// Rules for a document body, read as CommonMark. The publisher splits the content by heading, so
/// headings are kept to the one unambiguous form whose section paths this computes.
/// </summary>
internal static class KnowledgeBody
{
    // Sections go down to h3; a deeper heading would read as part of its h3 section.
    private const int DeepestHeading = 3;

    // Without precise locations, inline nodes such as links all report line 0.
    internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    /// <summary>Checks the body and returns its section paths, in document order.</summary>
    internal static IReadOnlyList<SectionPath> Validate(string body, string title, string path, int firstLine, List<string> errors)
    {
        var document = Markdown.Parse(body, Pipeline);
        string Where(MarkdownObject node) => $"{path}:{firstLine + node.Line}";

        if (document.FirstOrDefault() is not HeadingBlock { Level: 1 })
        {
            errors.Add($"{path}: the body must start with a level-1 heading, the title.");
        }

        var sections = new List<SectionPath>();
        string? h1 = null, h2 = null;
        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case HeadingBlock heading when heading.Parent != document:
                    errors.Add($"{Where(heading)}: headings must not be inside lists, quotes or tables.");
                    break;

                case HeadingBlock { IsSetext: true } heading:
                    errors.Add($"{Where(heading)}: write headings with '#'; underlined headings aren't allowed.");
                    break;

                case HeadingBlock heading:
                    if (PlainText(heading) is not { } text)
                    {
                        errors.Add($"{Where(heading)}: headings must be plain text, without links, code or emphasis.");
                        break;
                    }

                    if (heading.Level == 1)
                    {
                        if (h1 is not null)
                        {
                            errors.Add($"{Where(heading)}: only one level-1 heading is allowed; it's the title.");
                        }
                        else if (text != title)
                        {
                            errors.Add($"{Where(heading)}: the level-1 heading '{text}' must match the title '{title}'.");
                        }

                        h1 = text;
                        h2 = null;
                        sections.Add(new SectionPath(text, string.Empty, string.Empty));
                    }
                    else if (heading.Level > DeepestHeading)
                    {
                        errors.Add($"{Where(heading)}: headings go down to level {DeepestHeading}; use a list or bold text below that.");
                    }
                    else if (h1 is null || (heading.Level == 3 && h2 is null))
                    {
                        errors.Add($"{Where(heading)}: a level-{heading.Level} heading needs a level-{heading.Level - 1} heading above it.");
                    }
                    else if (heading.Level == 2)
                    {
                        h2 = text;
                        sections.Add(new SectionPath(h1, text, string.Empty));
                    }
                    else
                    {
                        sections.Add(new SectionPath(h1, h2!, text));
                    }

                    break;

                case HtmlBlock or HtmlInline:
                    errors.Add($"{Where(node)}: HTML would be indexed as raw markup; use Markdown.");
                    break;

                case LinkInline { IsImage: true }:
                    errors.Add($"{Where(node)}: images aren't allowed; the corpus is text only.");
                    break;

                case LinkInline link when !IsAbsoluteWebUrl(link.Url):
                    errors.Add($"{Where(node)}: link '{link.Url}' must be an absolute https URL; relative links don't resolve outside the repository.");
                    break;

                case AutolinkInline { IsEmail: false } autolink when !IsAbsoluteWebUrl(autolink.Url):
                    errors.Add($"{Where(node)}: link '{autolink.Url}' must be an absolute https URL.");
                    break;
            }
        }

        foreach (var duplicate in sections.GroupBy(s => s).Where(g => g.Count() > 1))
        {
            errors.Add($"{path}: section '{duplicate.Key}' appears more than once; citations need each path to be unique.");
        }

        return sections;
    }

    // Literal text only, so the heading reads the same in the source, the index and a citation.
    internal static string? PlainText(HeadingBlock heading)
    {
        if (heading.Inline is null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var inline in heading.Inline)
        {
            if (inline is not LiteralInline literal)
            {
                return null;
            }

            parts.Add(literal.Content.ToString());
        }

        var text = string.Concat(parts).Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsAbsoluteWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
