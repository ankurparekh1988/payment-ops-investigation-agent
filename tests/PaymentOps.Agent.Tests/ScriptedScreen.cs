using PaymentOps.Agent.Safety;

namespace PaymentOps.Agent.Tests;

/// <summary>A screen whose verdicts and timing a test decides, recording what it was asked.</summary>
internal sealed class ScriptedScreen(Func<string, ScreenVerdict> verdict, TimeSpan delay = default, TimeProvider? time = null) : IUntrustedTextScreen
{
    public static ScriptedScreen AllClean => new(_ => ScreenVerdict.Clean);

    public static ScriptedScreen Flagging(string marker) =>
        new(text => text.Contains(marker, StringComparison.Ordinal) ? ScreenVerdict.Attack : ScreenVerdict.Clean);

    /// <summary>Never answers; only cancellation ends the call.</summary>
    public static ScriptedScreen Hanging(TimeProvider time) => new(_ => ScreenVerdict.Clean, Timeout.InfiniteTimeSpan, time);

    public List<string> Screened { get; } = [];

    public async Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken)
    {
        lock (Screened)
        {
            Screened.AddRange(segments);
        }

        if (delay != default)
        {
            await Task.Delay(delay, time ?? TimeProvider.System, cancellationToken);
        }

        return [.. segments.Select(verdict)];
    }
}
