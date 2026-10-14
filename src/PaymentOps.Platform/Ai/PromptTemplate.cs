using System.Text;
using System.Text.RegularExpressions;

namespace PaymentOps.Platform.Ai;

/// <summary>
/// A prompt body with <c>{{name}}</c> placeholders, where a name is a letter followed by letters or
/// digits. That is the whole language: no conditionals, loops, expressions or includes. Every
/// <c>{{</c> must start a valid placeholder; a lone <c>}}</c> is ordinary text, as in nested JSON.
/// Values are inserted as data in a single pass, so a value that contains <c>{{x}}</c> stays literal.
/// </summary>
public sealed partial class PromptTemplate
{
    private readonly IReadOnlyList<(string Text, bool IsPlaceholder)> _segments;

    private PromptTemplate(IReadOnlyList<(string Text, bool IsPlaceholder)> segments)
    {
        _segments = segments;
        Variables = segments.Where(s => s.IsPlaceholder).Select(s => s.Text).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> Variables { get; }

    /// <summary>Parses a template; returns null and an error when a placeholder is malformed.</summary>
    internal static PromptTemplate? Parse(string text, string path, List<string> errors)
    {
        var segments = new List<(string, bool)>();
        var position = 0;

        while (position < text.Length)
        {
            var open = text.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0)
            {
                segments.Add((text[position..], false));
                break;
            }

            var close = text.IndexOf("}}", open + 2, StringComparison.Ordinal);
            var name = close < 0 ? null : text[(open + 2)..close];
            if (name is null || !Name().IsMatch(name))
            {
                var line = text.AsSpan(0, open).Count('\n') + 1;
                errors.Add($"{path} has a malformed placeholder on line {line} of its body; use {{{{name}}}} with letters and digits only.");
                return null;
            }

            if (open > position)
            {
                segments.Add((text[position..open], false));
            }

            segments.Add((name, true));
            position = close + 2;
        }

        return new PromptTemplate(segments);
    }

    /// <summary>Fills every placeholder. Fails if a value is missing or one is supplied that isn't used.</summary>
    public string Render(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var missing = Variables.Where(v => !values.ContainsKey(v)).Order(StringComparer.Ordinal).ToList();
        var unused = values.Keys.Where(k => !Variables.Contains(k)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || unused.Count > 0)
        {
            throw new ArgumentException(
                $"Template values don't match its placeholders. Missing: [{string.Join(", ", missing)}]; unused: [{string.Join(", ", unused)}].",
                nameof(values));
        }

        var output = new StringBuilder();
        foreach (var (text, isPlaceholder) in _segments)
        {
            output.Append(isPlaceholder ? values[text] : text);
        }

        return output.ToString();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
