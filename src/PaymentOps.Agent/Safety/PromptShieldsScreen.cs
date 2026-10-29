using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Azure.Core;
using Azure.Identity;

namespace PaymentOps.Agent.Safety;

/// <summary>
/// Screens untrusted text with Prompt Shields, as documents, keyless. The stable Azure.AI.ContentSafety package has no
/// Prompt Shields operation, so this calls the REST API. Text longer than one document is split into overlapping
/// pieces, so an instruction across a boundary is whole in at least one; any flagged piece flags the text.
/// </summary>
internal sealed class PromptShieldsScreen(HttpClient http, Uri endpoint, string tokenScope, TokenCredential credential, ScreenCache cache) : IUntrustedTextScreen
{
    public const int MaxDocuments = 5;
    public const int MaxCharsPerCall = 10_000;
    public const int MaxPieceChars = 9_500;
    public const int OverlapChars = 500;

    private readonly Uri _shieldPrompt = new(endpoint, "contentsafety/text:shieldPrompt?api-version=2024-09-01");

    // Managed identity caches its tokens, but a developer's Azure CLI sign-in starts a process for each one, which
    // would spend the screening budget. Two calls refreshing at once both fetch one; that's harmless.
    private volatile CachedToken? _token;

    public async Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var verdicts = new ScreenVerdict?[segments.Count];
        var pieces = new List<(int Segment, string Text)>();
        for (var i = 0; i < segments.Count; i++)
        {
            if (cache.TryGet(segments[i], out var cached))
            {
                verdicts[i] = cached;
                continue;
            }

            pieces.AddRange(Split(segments[i]).Select(piece => (i, piece)));
        }

        // Calls run together: they share one deadline, and a slow one shouldn't cost the others their time.
        var batches = Batches(pieces).ToList();
        var results = await Task.WhenAll(batches.Select(batch => CallAsync([.. batch.Select(p => p.Text)], cancellationToken)));
        var pieceVerdicts = batches.Zip(results).SelectMany(b => b.First.Select((p, j) => (p.Segment, Verdict: b.Second[j])));

        foreach (var group in pieceVerdicts.GroupBy(p => p.Segment))
        {
            var verdict = Aggregate([.. group.Select(p => p.Verdict)]);
            verdicts[group.Key] = verdict;
            cache.Set(segments[group.Key], verdict);
        }

        // Empty text has no pieces to send and carries nothing to screen.
        return [.. verdicts.Select(v => v ?? ScreenVerdict.Clean)];
    }

    /// <summary>Attack wins over Unavailable, which wins over Clean: text is only clean if every piece is.</summary>
    public static ScreenVerdict Aggregate(IReadOnlyList<ScreenVerdict> pieces) =>
        pieces.Contains(ScreenVerdict.Attack) ? ScreenVerdict.Attack
        : pieces.Contains(ScreenVerdict.Unavailable) ? ScreenVerdict.Unavailable
        : ScreenVerdict.Clean;

    private static IEnumerable<string> Split(string text)
    {
        for (var start = 0; start < text.Length; start += MaxPieceChars - OverlapChars)
        {
            yield return text.Substring(start, Math.Min(MaxPieceChars, text.Length - start));
            if (start + MaxPieceChars >= text.Length)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<List<(int Segment, string Text)>> Batches(List<(int Segment, string Text)> pieces)
    {
        var batch = new List<(int Segment, string Text)>();
        var chars = 0;
        foreach (var piece in pieces)
        {
            if (batch.Count == MaxDocuments || chars + piece.Text.Length > MaxCharsPerCall)
            {
                yield return batch;
                batch = [];
                chars = 0;
            }

            batch.Add(piece);
            chars += piece.Text.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private async Task<IReadOnlyList<ScreenVerdict>> CallAsync(IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        try
        {
            var token = await TokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, _shieldPrompt)
            {
                Content = JsonContent.Create(new { userPrompt = string.Empty, documents }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return UnavailableScreen.AllUnavailable(documents.Count);
            }

            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var analysis = body.RootElement.GetProperty("documentsAnalysis");
            return analysis.GetArrayLength() == documents.Count
                ? [.. analysis.EnumerateArray().Select(d => d.GetProperty("attackDetected").GetBoolean() ? ScreenVerdict.Attack : ScreenVerdict.Clean)]
                : UnavailableScreen.AllUnavailable(documents.Count);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or TaskCanceledException or AuthenticationFailedException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return UnavailableScreen.AllUnavailable(documents.Count);
        }
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (_token is { } cached && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return cached.Value;
        }

        var fresh = await credential.GetTokenAsync(new TokenRequestContext([tokenScope]), cancellationToken);
        _token = new CachedToken(fresh.Token, fresh.ExpiresOn);
        return fresh.Token;
    }

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresOn);
}
