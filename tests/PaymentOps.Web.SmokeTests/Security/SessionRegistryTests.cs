using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

public sealed class SessionRegistryTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private const string User = "user-1";

    private readonly FakeTimeProvider _time = new(T);
    private readonly SessionRegistry _registry;

    public SessionRegistryTests() => _registry = new SessionRegistry(_time, Options.Create(new AccessOptions()));

    private SessionRecord Add(string sid, TimeSpan? lifetime = null, DateTimeOffset? authenticatedAt = null)
    {
        var record = new SessionRecord(sid, User, "tenant-1", _time.GetUtcNow() + (lifetime ?? TimeSpan.FromHours(8)), _time.GetUtcNow(), authenticatedAt, "fp");
        _registry.Add(record);
        return record;
    }

    private void At(TimeSpan offset) => _time.SetUtcNow(T + offset);

    [Fact]
    public void Idle_boundaries()
    {
        Add("a");
        Add("b");

        At(new TimeSpan(0, 12, 59));
        var status = _registry.Status("a", User);
        Assert.True(status.IsLive);
        Assert.Equal(new TimeSpan(0, 2, 1), status.IdleRemaining);

        At(new TimeSpan(0, 13, 0));
        status = _registry.Status("a", User);
        Assert.True(status.IsLive);
        Assert.Equal(TimeSpan.FromMinutes(2), status.IdleRemaining);

        At(new TimeSpan(0, 14, 59));
        Assert.True(_registry.Touch("a", User));
        Assert.True(_registry.Status("b", User).IsLive);
        At(new TimeSpan(0, 15, 0));
        Assert.False(_registry.Status("b", User).IsLive);
        At(new TimeSpan(0, 29, 58));
        Assert.True(_registry.Status("a", User).IsLive);
        At(new TimeSpan(0, 29, 59));
        Assert.False(_registry.Status("a", User).IsLive);

    }

    [Fact]
    public void Touch_after_expiry_does_not_revive()
    {
        Add("a");

        At(TimeSpan.FromMinutes(15));

        Assert.False(_registry.Touch("a", User));
        Assert.False(_registry.Status("a", User).IsLive);
    }

    [Fact]
    public void Absolute_limit_is_not_extended_by_activity()
    {
        Add("a");

        for (var minute = 1; minute < 480; minute++)
        {
            At(TimeSpan.FromMinutes(minute));
            Assert.True(_registry.Touch("a", User));
        }

        At(new TimeSpan(7, 59, 59));
        Assert.True(_registry.Status("a", User).IsLive);

        At(TimeSpan.FromHours(8));
        Assert.False(_registry.Status("a", User).IsLive);
        Assert.False(_registry.Touch("a", User));
    }

    [Fact]
    public void Status_requires_the_same_user()
    {
        Add("a");

        Assert.True(_registry.Status("a", User).IsLive);
        Assert.False(_registry.Status("a", "someone-else").IsLive);
        Assert.False(_registry.Status("a", null).IsLive);
        Assert.False(_registry.Status(null, User).IsLive);
        Assert.False(_registry.Touch("a", "someone-else"));
    }

    [Fact]
    public void Records_past_absolute_expiry_are_pruned_on_write()
    {
        Add("first");

        At(TimeSpan.FromHours(8));
        Add("second");

        Assert.Null(_registry.Find("first"));
        Assert.NotNull(_registry.Find("second"));
    }

    [Fact]
    public void A_duplicate_sid_is_rejected()
    {
        Add("a");

        Assert.Throws<InvalidOperationException>(() => Add("a"));
    }

    [Fact]
    public void RecordAuthentication_only_on_a_live_session()
    {
        Add("a");

        Assert.False(_registry.RecordAuthentication("a", "someone-else", T));
        Assert.False(_registry.RecordAuthentication("missing", User, T));
        Assert.True(_registry.RecordAuthentication("a", User, T));
        Assert.Equal(T, _registry.Find("a")!.AuthenticatedAt);

        At(TimeSpan.FromMinutes(15));
        Assert.False(_registry.RecordAuthentication("a", User, T + TimeSpan.FromMinutes(15)));
        Assert.Equal(T, _registry.Find("a")!.AuthenticatedAt);
    }

    [Fact]
    public void RecordAuthentication_is_monotonic()
    {
        Add("a");

        Assert.True(_registry.RecordAuthentication("a", User, T.AddMinutes(-1)));
        Assert.True(_registry.RecordAuthentication("a", User, T.AddMinutes(-5)));

        Assert.Equal(T.AddMinutes(-1), _registry.Find("a")!.AuthenticatedAt);
        Assert.Equal(T.AddMinutes(-1), _registry.Status("a", User).AuthenticatedAt);
    }

    [Fact]
    public void Removed_session_is_never_recreated()
    {
        for (var i = 0; i < 1000; i++)
        {
            var sid = $"race-{i}";
            Add(sid);

            Parallel.Invoke(
                () => _registry.Touch(sid, User),
                () => _registry.RecordAuthentication(sid, User, T),
                () => _registry.Remove(sid));

            Assert.Null(_registry.Find(sid));
            Assert.False(_registry.Status(sid, User).IsLive);
        }
    }

    [Fact]
    public void Lost_race_rechecks_liveness()
    {
        var hooked = new HookedTimeProvider(_time);
        var registry = new SessionRegistry(hooked, Options.Create(new AccessOptions()));
        registry.Add(new SessionRecord("a", User, "tenant-1", T.AddHours(8), T, null, "fp"));

        // The clock is read between reading the record and swapping it, so this removal beats the swap.
        hooked.OnRead = () => registry.Remove("a");

        Assert.False(registry.Touch("a", User));
        Assert.Null(registry.Find("a"));
    }

    private sealed class HookedTimeProvider(TimeProvider inner) : TimeProvider
    {
        public Action? OnRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            var hook = OnRead;
            OnRead = null;
            hook?.Invoke();
            return inner.GetUtcNow();
        }
    }
}
