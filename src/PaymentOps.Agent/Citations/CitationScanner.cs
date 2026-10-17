using System.Text.RegularExpressions;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PaymentOps.Agent.Citations;

/// <summary>
/// The one definition of a citation, shared by answer validation and rendering: an exact
/// <c>[S&lt;n&gt;]</c> in ordinary text, never inside a link, image, autolink, HTML, entity or code.
/// </summary>
public static partial class CitationScanner
{
    /// <summary>CommonMark with no extensions; raw HTML still parses into nodes so it can be shown as text.</summary>
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder().Build();

    public static CitationScan Scan(string markdown) => Scan(Markdown.Parse(markdown, Pipeline));

    public static CitationScan Scan(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var citations = new List<CitationToken>();
        var malformed = false;
        foreach (var container in Containers(document).Where(IsOrdinaryText))
        {
            foreach (var run in Segments(container).Select(segment => segment.Run).OfType<TextRun>())
            {
                foreach (Match match in Exact().Matches(run.Text))
                {
                    citations.Add(new CitationToken(match.Groups[1].Value, run, match.Index, match.Length));
                }

                malformed |= NearMiss().Matches(run.Text).Any(m => !ExactWhole().IsMatch(m.Value));
            }
        }

        return new CitationScan(citations, malformed);
    }

    /// <summary>
    /// A container's children as text runs and other inlines. Adjacent literals are joined, and an
    /// unresolved bracket (Markdig keeps it as a link delimiter) is just "[" followed by its children,
    /// so "[S1]" reads the same however Markdig split it.
    /// </summary>
    public static IEnumerable<InlineSegment> Segments(ContainerInline container)
    {
        ArgumentNullException.ThrowIfNull(container);

        var pieces = new List<(Inline Source, string Text)>();
        foreach (var inline in Flatten(container))
        {
            if (inline.Text is not null)
            {
                pieces.Add((inline.Source, inline.Text));
                continue;
            }

            if (pieces.Count > 0)
            {
                yield return new InlineSegment(TextRun.Of(pieces), null);
                pieces = [];
            }

            yield return new InlineSegment(null, inline.Source);
        }

        if (pieces.Count > 0)
        {
            yield return new InlineSegment(TextRun.Of(pieces), null);
        }
    }

    private static IEnumerable<(Inline Source, string? Text)> Flatten(ContainerInline container)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    yield return (literal, literal.Content.ToString());
                    break;
                case LinkDelimiterInline delimiter:
                    yield return (delimiter, delimiter.ToLiteral());
                    foreach (var child in Flatten(delimiter))
                    {
                        yield return child;
                    }

                    break;
                default:
                    yield return (inline, null);
                    break;
            }
        }
    }

    // Each leaf block's root inline and everything nested in it; the document walk skips the roots.
    private static IEnumerable<ContainerInline> Containers(MarkdownDocument document) =>
        document.Descendants<LeafBlock>()
            .Select(leaf => leaf.Inline)
            .OfType<ContainerInline>()
            .SelectMany(root => root.Descendants<ContainerInline>().Prepend(root))
            .Where(container => container is not LinkDelimiterInline);

    // Text under a link (its label) or an image isn't ordinary text.
    private static bool IsOrdinaryText(ContainerInline container)
    {
        for (Inline? node = container; node is not null; node = node.Parent)
        {
            if (node is LinkInline)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"\[(S[1-9][0-9]*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex Exact();

    [GeneratedRegex(@"^\[S[1-9][0-9]*\]$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactWhole();

    // Anything bracketed that starts like a citation: [s1], [S01], [S1, S2], [S1 ], [S0].
    [GeneratedRegex(@"\[\s*[Ss]\s*\d[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex NearMiss();
}

public sealed record CitationScan(IReadOnlyList<CitationToken> Citations, bool HasMalformedMarker);

/// <summary>A citation inside a run: <paramref name="Start"/> and <paramref name="Length"/> are offsets in the run's text.</summary>
public sealed record CitationToken(string Id, TextRun Run, int Start, int Length);

/// <summary>Either a run of text or another inline, in document order.</summary>
public sealed record InlineSegment(TextRun? Run, Inline? Other);

/// <summary>Text from adjacent literals and unresolved brackets, joined.</summary>
public sealed record TextRun(IReadOnlyList<Inline> Sources, string Text)
{
    public Inline First => Sources[0];

    internal static TextRun Of(List<(Inline Source, string Text)> pieces) =>
        new([.. pieces.Select(p => p.Source)], string.Concat(pieces.Select(p => p.Text)));
}
