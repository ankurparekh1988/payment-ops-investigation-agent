using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Web.Components.Pages;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Usage;

/// <summary>
/// The guard stands between a person and an investigation: paused, over a limit or unable to count, nothing runs and
/// nothing is reserved; admitted, the attempt counts whatever happens next. A refusal is counted by reason only.
/// </summary>
[Collection(nameof(UsageGuardTests))]
[CollectionDefinition(nameof(UsageGuardTests), DisableParallelization = true)]
public sealed class UsageGuardTests : IDisposable
{
    private const string UserSentinel = "zzq-user";

    private readonly List<KeyValuePair<string, object?>[]> _rejections = [];
    private readonly MeterListener _listener = new();

    public UsageGuardTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AgentTelemetry.Name && instrument.Name == "paymentops.investigations.rejected")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            lock (_rejections)
            {
                _rejections.Add(tags.ToArray());
            }
        });
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    private sealed class CountingService(Func<AssessmentResult>? answer = null) : IInvestigationService
    {
        public int Calls { get; private set; }

        public ConversationStatus Conversation => new(0, 0, false);

        public Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer?.Invoke() ?? new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable));
        }

        public void StartNew()
        {
        }

        public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger<UsageGuard>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + " " + string.Join(' ', (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(p => p.Value) ?? []));
    }

    private sealed class Monitor(UsageOptions value) : IOptionsMonitor<UsageOptions>
    {
        public UsageOptions CurrentValue => value;

        public UsageOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<UsageOptions, string?> listener) => null;
    }

    private static (UsageGuard Guard, CountingService Inner, FakeTableClient Table, RecordingLogger Logger) Guard(
        UsageOptions? options = null, FakeTableClient? table = null, TimeProvider? clock = null, Func<AssessmentResult>? answer = null, IPauseStore? pause = null)
    {
        var inner = new CountingService(answer);
        var fake = table ?? new FakeTableClient();
        var logger = new RecordingLogger();
        var guard = new UsageGuard(
            inner,
            new TableUsageCounter(fake, TableUsageCounter.DefaultMaxAttempts, _ => TimeSpan.Zero),
            pause ?? new InMemoryPauseStore(),
            TestUser.Reader with { UserId = UserSentinel },
            new Monitor(options ?? new UsageOptions { DailyLimitPerUser = 2, DailyLimitTotal = 10 }),
            clock ?? new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)),
            logger);
        return (guard, inner, fake, logger);
    }

    private static NotAssessedReason Reason(AssessmentResult result) => Assert.IsType<AssessmentResult.NotAssessed>(result).Reason;

    [Fact]
    public async Task Paused_reserves_nothing_and_runs_nothing()
    {
        var (guard, inner, table, _) = Guard(new UsageOptions { Paused = true, DailyLimitPerUser = 2, DailyLimitTotal = 10 });

        Assert.Equal(NotAssessedReason.Paused, Reason(await guard.InvestigateAsync("q", null, CancellationToken.None)));

        Assert.Equal(0, inner.Calls);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public async Task Over_the_user_limit_is_refused_and_runs_nothing()
    {
        var (guard, inner, _, _) = Guard();

        await guard.InvestigateAsync("q", null, CancellationToken.None);
        await guard.InvestigateAsync("q", null, CancellationToken.None);
        var third = await guard.InvestigateAsync("q", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.DailyLimitReached, Reason(third));
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task An_admitted_attempt_counts_even_if_the_investigation_fails()
    {
        var (guard, _, table, _) = Guard(answer: () => throw new InvalidOperationException("broke"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.InvestigateAsync("q", null, CancellationToken.None));

        Assert.Equal(1, table.Count("2026-11-14", TableUsageCounter.TotalRow));
    }

    [Fact]
    public async Task Attempts_reserve_against_the_UTC_day_they_were_made()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 23, 59, 59, TimeSpan.Zero));
        var (guard, _, table, _) = Guard(new UsageOptions { DailyLimitPerUser = 1, DailyLimitTotal = 1 }, clock: clock);

        await guard.InvestigateAsync("q", null, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        var next = await guard.InvestigateAsync("q", null, CancellationToken.None);

        Assert.NotEqual(NotAssessedReason.DailyLimitReached, Reason(next));
        Assert.Equal(1, table.Count("2026-11-14", TableUsageCounter.TotalRow));
        Assert.Equal(1, table.Count("2026-11-15", TableUsageCounter.TotalRow));
    }

    [Fact]
    public async Task Unavailable_usage_refuses_without_logging_the_user()
    {
        var (guard, inner, _, logger) = Guard(table: new FakeTableClient { ReadsThrow = new Azure.RequestFailedException(503, "read failed") });

        Assert.Equal(NotAssessedReason.UsageUnavailable, Reason(await guard.InvestigateAsync("q", null, CancellationToken.None)));

        Assert.Equal(0, inner.Calls);
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, m => m.Contains(UserSentinel, StringComparison.Ordinal));
    }

    // The page catches nothing: a Cancel during the usage check must come back as a result, as it does once running.
    [Fact]
    public async Task Cancel_during_the_usage_check_is_a_cancelled_result()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var (guard, inner, table, _) = Guard(table: new FakeTableClient { ReadsThrow = new OperationCanceledException(cancel.Token) });

        Assert.Equal(NotAssessedReason.Cancelled, Reason(await guard.InvestigateAsync("q", null, cancel.Token)));

        Assert.Equal(0, inner.Calls);
        Assert.Empty(table.Rows);
    }

    public static TheoryData<string, NotAssessedReason, string> Rejections() => new()
    {
        { "paused", NotAssessedReason.Paused, "paused" },
        { "over the limit", NotAssessedReason.DailyLimitReached, "daily_limit" },
        { "usage unavailable", NotAssessedReason.UsageUnavailable, "usage_unavailable" },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task Each_rejection_is_counted_by_reason_only(string row, NotAssessedReason reason, string tag)
    {
        var (guard, _, _, _) = reason switch
        {
            NotAssessedReason.Paused => Guard(new UsageOptions { Paused = true, DailyLimitPerUser = 1, DailyLimitTotal = 1 }),
            NotAssessedReason.UsageUnavailable => Guard(table: new FakeTableClient { ReadsThrow = new Azure.RequestFailedException(503, "read failed") }),
            _ => Guard(new UsageOptions { DailyLimitPerUser = 1, DailyLimitTotal = 1 }),
        };
        if (reason == NotAssessedReason.DailyLimitReached)
        {
            await guard.InvestigateAsync("q", null, CancellationToken.None);
        }

        Assert.Equal(reason, Reason(await guard.InvestigateAsync("zzq-question", null, CancellationToken.None)));

        var counted = Assert.Single(_rejections);
        Assert.True(counted.SequenceEqual([new KeyValuePair<string, object?>("reason", tag)]), row);
    }

    [Theory]
    [InlineData(NotAssessedReason.Paused, "Investigations are paused.")]
    [InlineData(NotAssessedReason.DailyLimitReached, "Today's investigation limit has been reached. It resets at 00:00 UTC.")]
    [InlineData(NotAssessedReason.UsageUnavailable, "Investigations can't start right now. Please try again shortly.")]
    public void Each_refusal_has_its_own_text(NotAssessedReason reason, string text) =>
        Assert.Equal(text, NotAssessedText.For(reason, 2000));

    [Fact]
    public async Task A_paused_switch_refuses_new_investigations()
    {
        var pause = new InMemoryPauseStore();
        await pause.WriteAsync(new PauseState(true, "u-admin", "Ada", DateTimeOffset.UnixEpoch, null), null, CancellationToken.None);
        var (guard, inner, table, _) = Guard(pause: pause);

        Assert.Equal(NotAssessedReason.Paused, Reason(await guard.InvestigateAsync("q", null, CancellationToken.None)));

        Assert.Equal(0, inner.Calls);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public async Task An_unreadable_pause_row_refuses_as_usage_unavailable()
    {
        var pauseTable = new FakeEntityTable { ReadsThrow = new Azure.RequestFailedException(503, "unavailable") };
        var (guard, inner, table, _) = Guard(pause: new TablePauseStore(pauseTable, TimeProvider.System));

        Assert.Equal(NotAssessedReason.UsageUnavailable, Reason(await guard.InvestigateAsync("q", null, CancellationToken.None)));

        Assert.Equal(0, inner.Calls);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public async Task Configuration_pause_wins_over_a_running_switch()
    {
        var (guard, inner, _, _) = Guard(new UsageOptions { Paused = true, DailyLimitPerUser = 2, DailyLimitTotal = 10 }, pause: new InMemoryPauseStore());

        Assert.Equal(NotAssessedReason.Paused, Reason(await guard.InvestigateAsync("q", null, CancellationToken.None)));
        Assert.Equal(0, inner.Calls);
    }

    // Cancel while the pause is being read is a cancellation, as it is while usage is counted, not an unhandled error.
    [Fact]
    public async Task Cancel_while_the_pause_is_read_is_reported_as_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var (guard, inner, table, _) = Guard(pause: new CancellingPauseStore());

        var result = await guard.InvestigateAsync("q", null, cancel.Token);

        Assert.Equal(NotAssessedReason.Cancelled, Reason(result));
        Assert.Equal(0, inner.Calls);
        Assert.Empty(table.Rows);
    }

    private sealed class CancellingPauseStore : IPauseStore
    {
        public Task<PauseState> ReadAsync(CancellationToken cancellationToken) => Task.FromCanceled<PauseState>(cancellationToken);

        public Task<PauseState> ReadFreshAsync(CancellationToken cancellationToken) => Task.FromCanceled<PauseState>(cancellationToken);

        public Task<PauseWrite> WriteAsync(PauseState next, string? expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
