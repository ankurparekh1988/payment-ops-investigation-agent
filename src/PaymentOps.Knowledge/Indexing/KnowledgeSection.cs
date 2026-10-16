using Markdig;
using Markdig.Syntax;

using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>A heading and the text below it, up to the next heading.</summary>
/// <param name="Ordinal">The heading's position among all of the document's headings, matching corpus.json.</param>
/// <param name="Content">The section's own Markdown, without headings.</param>
public sealed record KnowledgeSection(SectionPath Path, int Ordinal, string Content)
{
    /// <summary>Only sections with text are indexed; an empty heading still anchors citations.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Content);
}

internal static class KnowledgeSections
{
    /// <summary>Why a document with no section text is refused, by the corpus build and again when publishing.</summary>
    public const string NothingToIndex = "no section has text of its own, so there's nothing to index.";

    /// <summary>
    /// Splits validated content with the same parse that validated it, so the sections indexed are
    /// the sections checked. Every heading is returned, with or without text.
    /// </summary>
    public static IReadOnlyList<KnowledgeSection> Split(string content)
    {
        // Validation keeps headings at the top level, so the document's direct children are all of them.
        var headings = Markdown.Parse(content, KnowledgeBody.Pipeline).OfType<HeadingBlock>().ToList();
        var sections = new List<KnowledgeSection>(headings.Count);
        string h1 = string.Empty, h2 = string.Empty;
        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            var text = KnowledgeBody.PlainText(heading) ?? string.Empty;
            SectionPath path;
            if (heading.Level == 1)
            {
                (h1, h2) = (text, string.Empty);
                path = new SectionPath(h1, string.Empty, string.Empty);
            }
            else if (heading.Level == 2)
            {
                h2 = text;
                path = new SectionPath(h1, h2, string.Empty);
            }
            else
            {
                path = new SectionPath(h1, h2, text);
            }

            var start = Math.Min(heading.Span.End + 1, content.Length);
            var end = i + 1 < headings.Count ? headings[i + 1].Span.Start : content.Length;

            // Only blank lines are trimmed: leading spaces can be Markdown (an indented code block).
            sections.Add(new KnowledgeSection(path, i, content[start..end].Trim('\n')));
        }

        return sections;
    }
}
