using System.Security.Claims;

using Azure;
using Azure.Data.Tables;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.SmokeTests.Usage;

/// <summary>
/// The Admin's pause: checked on every call, not by hiding a button; stored in the usage table so every instance sees
/// it within the cache lifetime and the instance that changed it at once; a lost race reports what's there now.
/// </summary>
public sealed class PauseSwitchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeEntityTable _table = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingLogger<PauseSwitch> _log = new();
    private readonly TestRecentAuthentication _recent = new();

    private static TestUser Admin(string id = "00000000-0000-0000-0000-00000000000a") =>
        TestUser.Engineer with { Roles = OpsRoles.Effective([OpsRoles.Admin]), UserId = id };

    private TablePauseStore Store() => new(_table, _time);

    private PauseSwitch Switch(IUserContext user, IPauseStore? store = null, IRecentAuthentication? recent = null) =>
        new(store ?? Store(), user, recent ?? _recent, _time, _log);

    [Theory]
    [InlineData("reader")]
    [InlineData("engineer")]
    [InlineData("anonymous")]
    public async Task Only_an_admin_can_change_the_pause(string who)
    {
        var user = who switch { "reader" => TestUser.Reader, "engineer" => TestUser.Engineer, _ => TestUser.Anonymous };

        var result = await Switch(user).SetAsync(true, "maintenance", CancellationToken.None);

        Assert.IsType<PauseResult.NotAuthorized>(result);
        Assert.Equal(0, _table.Writes);
        Assert.Null(_table.Row(TablePauseStore.Partition, TablePauseStore.RowKey));
    }

    [Fact]
    public async Task No_row_means_running()
    {
        var state = await Store().ReadAsync(CancellationToken.None);

        Assert.False(state.Paused);
    }

    [Fact]
    public async Task An_admin_pauses_with_who_when_and_why()
    {
        var result = await Switch(Admin()).SetAsync(true, "Processor maintenance", CancellationToken.None);

        var state = Assert.IsType<PauseResult.Changed>(result).State;
        Assert.True(state.Paused);
        Assert.Equal(TableUsageCounter.UserRow(Admin().UserId), state.ChangedBy);
        Assert.Equal("Test", state.ChangedByName);
        Assert.Equal(Now, state.ChangedAt);
        Assert.Equal("Processor maintenance", state.Reason);
        Assert.Equal(true, _table.Row(TablePauseStore.Partition, TablePauseStore.RowKey)!.GetBoolean("Paused"));
    }

    [Fact]
    public async Task The_reason_is_kept_as_plain_text_up_to_200_chars()
    {
        var reason = "<b>maintenance</b> " + new string('x', 300);

        var state = Assert.IsType<PauseResult.Changed>(await Switch(Admin()).SetAsync(true, reason, CancellationToken.None)).State;

        Assert.Equal(200, state.Reason!.Length);
        Assert.StartsWith("<b>maintenance</b>", state.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blank_reason_is_stored_as_none_and_resume_takes_no_reason()
    {
        var store = Store();
        await Switch(Admin(), store).SetAsync(true, "   ", CancellationToken.None);

        var resumed = Assert.IsType<PauseResult.Changed>(await Switch(Admin(), store).SetAsync(false, "ignored", CancellationToken.None)).State;

        Assert.False(resumed.Paused);
        Assert.Null(resumed.Reason);
    }

    [Fact]
    public async Task A_change_is_seen_at_once_on_the_initiating_instance()
    {
        var store = Store();
        Assert.False((await store.ReadAsync(CancellationToken.None)).Paused);

        await Switch(Admin(), store).SetAsync(true, null, CancellationToken.None);

        Assert.True((await store.ReadAsync(CancellationToken.None)).Paused);
    }

    [Fact]
    public async Task Another_instance_sees_the_change_when_its_cache_expires()
    {
        var mine = Store();
        var other = Store();
        Assert.False((await other.ReadAsync(CancellationToken.None)).Paused);

        await Switch(Admin(), mine).SetAsync(true, null, CancellationToken.None);

        Assert.False((await other.ReadAsync(CancellationToken.None)).Paused);
        _time.Advance(TablePauseStore.CacheLifetime);
        Assert.True((await other.ReadAsync(CancellationToken.None)).Paused);
    }

    // The lifetime is measured on the monotonic clock: setting the wall clock back mustn't keep a stale state.
    [Fact]
    public async Task The_cache_expires_by_elapsed_time_when_the_wall_clock_goes_back()
    {
        var clock = new SteppingClock(Now);
        var mine = new TablePauseStore(_table, clock);
        var other = new TablePauseStore(_table, clock);
        Assert.False((await other.ReadAsync(CancellationToken.None)).Paused);

        await Switch(Admin(), mine).SetAsync(true, null, CancellationToken.None);
        clock.Step(wallClock: TimeSpan.FromHours(-1), elapsed: TablePauseStore.CacheLifetime);

        Assert.True((await other.ReadAsync(CancellationToken.None)).Paused);
    }

    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Step(TimeSpan wallClock, TimeSpan elapsed)
        {
            _now += wallClock;
            _ticks += elapsed.Ticks;
        }
    }

    // A read that started before a change and finished after it mustn't put the old state back in the cache.
    [Fact]
    public async Task A_slow_read_never_overwrites_a_newer_change()
    {
        var store = Store();
        _table.HoldReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = store.ReadFreshAsync(CancellationToken.None);

        Assert.IsType<PauseWrite.Written>(await store.WriteAsync(PauseState.Running with { Paused = true }, null, CancellationToken.None));
        _table.HoldReads.SetResult();
        _table.HoldReads = null;
        await slow;

        Assert.True((await store.ReadAsync(CancellationToken.None)).Paused);
    }

    [Fact]
    public async Task A_concurrent_change_reports_the_current_state()
    {
        var first = Store();
        var second = Store();
        await first.ReadAsync(CancellationToken.None);
        await second.ReadAsync(CancellationToken.None);

        Assert.IsType<PauseResult.Changed>(await Switch(Admin("a"), first).SetAsync(true, "first", CancellationToken.None));
        var late = await Switch(Admin("b"), second).SetAsync(true, "second", CancellationToken.None);

        var current = Assert.IsType<PauseResult.Conflict>(late).Current;
        Assert.True(current.Paused);
        Assert.Equal("first", current.Reason);
    }

    [Fact]
    public async Task A_write_that_loses_the_race_reports_the_winner()
    {
        var store = new RacingStore(Store(), _table);

        var result = await Switch(Admin(), store).SetAsync(true, "mine", CancellationToken.None);

        Assert.Equal("theirs", Assert.IsType<PauseResult.Conflict>(result).Current.Reason);
    }

    [Fact]
    public async Task A_failed_write_changes_nothing_and_refreshes_the_cache()
    {
        var store = Store();
        await store.ReadAsync(CancellationToken.None);
        _table.WritesThrow = new RequestFailedException(503, "unavailable");
        var reads = _table.Reads;

        var result = await Switch(Admin(), store).SetAsync(true, null, CancellationToken.None);

        Assert.IsType<PauseResult.Unavailable>(result);
        Assert.Null(_table.Row(TablePauseStore.Partition, TablePauseStore.RowKey));
        _table.WritesThrow = null;
        await store.ReadAsync(CancellationToken.None);
        Assert.True(_table.Reads > reads + 1, "the next read goes to the table, not the cache");
    }

    [Fact]
    public async Task An_unreadable_row_fails_closed()
    {
        _table.ReadsThrow = new RequestFailedException(503, "unavailable");

        await Assert.ThrowsAsync<PauseUnavailableException>(() => Store().ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_malformed_row_fails_closed()
    {
        _table.Put(new TableEntity(TablePauseStore.Partition, TablePauseStore.RowKey) { ["Paused"] = "yes" });

        await Assert.ThrowsAsync<PauseUnavailableException>(() => Store().ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Each_change_is_logged_without_the_reason_text()
    {
        await Switch(Admin()).SetAsync(true, "Processor maintenance window", CancellationToken.None);

        var entry = Assert.Single(_log.Entries);
        Assert.Equal(3301, entry.EventId.Id);
        Assert.Contains("True", entry.Text, StringComparison.Ordinal);
        Assert.Contains(TableUsageCounter.UserRow(Admin().UserId), entry.Text, StringComparison.Ordinal);
        Assert.Contains("28", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("maintenance", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Test", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_in_memory_store_behaves_the_same()
    {
        var store = new InMemoryPauseStore();

        var changed = await Switch(Admin(), store).SetAsync(true, "why", CancellationToken.None);
        var again = await Switch(Admin(), store).SetAsync(true, "again", CancellationToken.None);

        Assert.True(Assert.IsType<PauseResult.Changed>(changed).State.Paused);
        Assert.Equal("why", Assert.IsType<PauseResult.Conflict>(again).Current.Reason);
        Assert.True((await store.ReadAsync(CancellationToken.None)).Paused);
    }

    [Fact]
    public async Task Stale_resume_is_refused_without_touching_the_store()
    {
        _recent.Recent = false;

        var result = await Switch(Admin()).SetAsync(false, null, CancellationToken.None);

        Assert.IsType<PauseResult.ReauthenticationRequired>(result);
        Assert.Equal(0, _table.Reads);
        Assert.Equal(0, _table.Writes);
    }

    // Pausing only stops new investigations, so an Admin in an incident isn't asked to sign in again.
    [Fact]
    public async Task Stale_admin_can_still_pause()
    {
        _recent.Recent = false;

        var result = await Switch(Admin()).SetAsync(true, "maintenance", CancellationToken.None);

        Assert.True(Assert.IsType<PauseResult.Changed>(result).State.Paused);
        Assert.Equal(1, _table.Writes);
    }

    [Fact]
    public async Task Fresh_admin_changes_the_pause()
    {
        var result = await Switch(Admin()).SetAsync(true, "maintenance", CancellationToken.None);

        Assert.True(Assert.IsType<PauseResult.Changed>(result).State.Paused);
        Assert.Equal(1, _table.Writes);
    }

    [Fact]
    public async Task Fresh_non_admin_is_not_authorized()
    {
        var result = await Switch(TestUser.Engineer).SetAsync(true, "maintenance", CancellationToken.None);

        Assert.IsType<PauseResult.NotAuthorized>(result);
        Assert.Equal(0, _table.Reads);
        Assert.Equal(0, _table.Writes);
    }

    // The checks before the read can pass and the session still end, or go stale, while a resume's read is pending.
    [Theory]
    [InlineData("ended")]
    [InlineData("stale")]
    public async Task Session_ending_during_the_store_read_writes_nothing(string during)
    {
        const string sid = "session-1";
        const string oid = "00000000-0000-0000-0000-00000000000a";
        var access = new AccessOptions();
        var registry = new SessionRegistry(_time, Options.Create(access));
        registry.Add(TestSessions.Started(sid, oid, Now, access));
        var accessor = new CurrentUserAccessor
        {
            User = TestPrincipal.From([new Claim("oid", oid), new Claim(SessionClaims.Sid, sid), new Claim("roles", OpsRoles.Admin)]),
        };
        var user = new ClaimsUserContext(accessor, Options.Create(access), registry);
        var recent = new RecentAuthenticationCheck(accessor, registry, _time, Options.Create(access));

        // Still recent when the change is asked for, with a minute of the window left.
        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.True(registry.Touch(sid, oid));
        var paused = new InMemoryPauseStore();
        await paused.WriteAsync(PauseState.Running with { Paused = true }, null, CancellationToken.None);
        var store = new GatedStore(paused);
        var change = Switch(user, store, recent).SetAsync(false, null, CancellationToken.None);
        await store.Reading.Task;

        if (during == "ended")
        {
            registry.Remove(sid);
        }
        else
        {
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        store.Gate.SetResult();
        var result = await change;

        if (during == "ended")
        {
            Assert.IsType<PauseResult.NotAuthorized>(result);
        }
        else
        {
            Assert.IsType<PauseResult.ReauthenticationRequired>(result);
        }

        Assert.Equal(0, store.Writes);
        Assert.True((await store.ReadAsync(CancellationToken.None)).Paused);
    }

    // Holds the fresh read until the test opens the gate.
    private sealed class GatedStore(IPauseStore inner) : IPauseStore
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Writes { get; private set; }

        public Task<PauseState> ReadAsync(CancellationToken cancellationToken) => inner.ReadAsync(cancellationToken);

        public async Task<PauseState> ReadFreshAsync(CancellationToken cancellationToken)
        {
            Reading.SetResult();
            await Gate.Task;
            return await inner.ReadFreshAsync(cancellationToken);
        }

        public Task<PauseWrite> WriteAsync(PauseState next, string? expectedVersion, CancellationToken cancellationToken)
        {
            Writes++;
            return inner.WriteAsync(next, expectedVersion, cancellationToken);
        }
    }

    // Another Admin's write lands between this switch's read and its write.
    private sealed class RacingStore(TablePauseStore inner, FakeEntityTable table) : IPauseStore
    {
        public Task<PauseState> ReadAsync(CancellationToken cancellationToken) => inner.ReadAsync(cancellationToken);

        public async Task<PauseState> ReadFreshAsync(CancellationToken cancellationToken)
        {
            var state = await inner.ReadFreshAsync(cancellationToken);
            table.Put(new TableEntity(TablePauseStore.Partition, TablePauseStore.RowKey) { ["Paused"] = true, ["Reason"] = "theirs" });
            return state;
        }

        public Task<PauseWrite> WriteAsync(PauseState next, string? expectedVersion, CancellationToken cancellationToken) =>
            inner.WriteAsync(next, expectedVersion, cancellationToken);
    }

}
