namespace PaymentOps.Agent.Safety;

/// <summary>
/// Adds harm checks to the screening of untrusted tool text: whatever Prompt Shields passes goes to Content Safety's
/// harm analysis and our classifier, as a question does, so harmful text is withheld like an instruction would be.
/// Each distinct text is checked once; text that couldn't be checked is never cached.
/// </summary>
internal sealed class HarmAwareScreen(IUntrustedTextScreen shields, ContentSafetyClient client, HarmClassifier classifier, ScreenCache cache) : IUntrustedTextScreen
{
    public async Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var verdicts = (await shields.ScreenAsync(segments, cancellationToken)).ToArray();
        var pending = segments.Where((segment, i) => verdicts[i] == ScreenVerdict.Clean && !cache.TryGet(segment, out _)).Distinct(StringComparer.Ordinal).ToList();
        var checkedTexts = await Task.WhenAll(pending.Select(async text => (text, Verdict: await CheckAsync(text, cancellationToken))));
        foreach (var (text, verdict) in checkedTexts.Where(c => c.Verdict != ScreenVerdict.Unavailable))
        {
            cache.Set(text, verdict);
        }

        var unanswered = checkedTexts.Where(c => c.Verdict == ScreenVerdict.Unavailable).Select(c => c.text).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < verdicts.Length; i++)
        {
            if (verdicts[i] == ScreenVerdict.Clean)
            {
                verdicts[i] = unanswered.Contains(segments[i]) ? ScreenVerdict.Unavailable
                    : cache.TryGet(segments[i], out var harm) ? harm
                    : ScreenVerdict.Clean;
            }
        }

        return verdicts;
    }

    // As for questions, a finding from either check stands even when the other couldn't answer.
    private async Task<ScreenVerdict> CheckAsync(string text, CancellationToken cancellationToken)
    {
        var analysis = HarmAnalysis.AnalyzeAsync(client, text, cancellationToken);
        var classified = classifier.ClassifyAsync(text, cancellationToken);
        var found = new[] { await analysis is { } severities ? HarmAnalysis.Verdict(severities) : QuestionVerdict.Unavailable, await classified };

        return found.Any(v => v is QuestionVerdict.Harmful or QuestionVerdict.SelfHarm) ? ScreenVerdict.Harmful
            : found.Contains(QuestionVerdict.Unavailable) ? ScreenVerdict.Unavailable
            : ScreenVerdict.Clean;
    }
}
