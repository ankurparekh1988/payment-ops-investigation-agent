namespace PaymentOps.Evaluation.Prompts;

/// <summary>
/// A prompt that lives with the evaluation code rather than in the AI release, versioned in its own header
/// (<c>id</c>, <c>version</c>) so every report can name the exact prompt it used.
/// </summary>
internal sealed record EvaluationPrompt(string Id, string Version, string Header, string Body)
{
    private const string Fence = "---";

    public static EvaluationPrompt Load(string resource)
    {
        using var stream = typeof(EvaluationPrompt).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The prompt resource '{resource}' isn't embedded.");
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().ReplaceLineEndings("\n").Split('\n');
        var end = Array.IndexOf(lines, Fence, 1);
        if (lines[0] != Fence || end < 0)
        {
            throw new InvalidOperationException($"The prompt '{resource}' has no front matter.");
        }

        var fields = lines[1..end].Select(l => l.Split(':', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);
        return new EvaluationPrompt(fields["id"], fields["version"], string.Join('\n', lines[1..end]), string.Join('\n', lines[(end + 1)..]).Trim());
    }
}
