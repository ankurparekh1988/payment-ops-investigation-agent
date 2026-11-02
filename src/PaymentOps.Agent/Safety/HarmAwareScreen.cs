namespace PaymentOps.Agent.Safety;

/// <summary>
/// Adds Content Safety's harm analysis to the screening of untrusted tool text: whatever Prompt Shields passes is
/// checked for hate, sexual content, violence and self-harm, so harmful text is withheld like an instruction would be.
/// Each distinct text is analysed once; text that couldn't be checked is never cached.
/// </summary>
internal sealed class HarmAwareScreen(IUntrustedTextScreen shields, ContentSafetyClient client, ScreenCache cache) : IUntrustedTextScreen
{
    public async Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var verdicts = (await shields.ScreenAsync(segments, cancellationToken)).ToArray();
        var pending = segments.Where((segment, i) => verdicts[i] == ScreenVerdict.Clean && !cache.TryGet(segment, out _)).Distinct(StringComparer.Ordinal).ToList();
        var analysed = await Task.WhenAll(pending.Select(async text => (text, Severities: await HarmAnalysis.AnalyzeAsync(client, text, cancellationToken))));
        foreach (var (text, severities) in analysed)
        {
            cache.Set(text, severities is null ? ScreenVerdict.Unavailable : HarmAnalysis.Verdict(severities) == QuestionVerdict.Clean ? ScreenVerdict.Clean : ScreenVerdict.Harmful);
        }

        var unanswered = analysed.Where(a => a.Severities is null).Select(a => a.text).ToHashSet(StringComparer.Ordinal);
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
}
