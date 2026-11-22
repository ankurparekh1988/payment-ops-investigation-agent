using System.Security.Claims;

using Bunit;
using Bunit.TestDoubles;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;

using MudBlazor;

using PaymentOps.Web.Components.Layout;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

/// <summary>The idle warning reads the server's registry; only the person's own input or Stay signed in keeps a session alive.</summary>
public sealed class IdleWatchTests : InteractiveTestContext
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private const string Sid = "sid-1";
    private const string Oid = "user-1";
    private const string Warning = "Signing you out soon";

    private readonly FakeTimeProvider _time = new(T);
    private readonly SessionRegistry _registry;
    private readonly CurrentUserAccessor _accessor = new();
    private readonly CountingRecentAuthentication _recent;

    public IdleWatchTests()
    {
        var access = Options.Create(new AccessOptions());
        _registry = new SessionRegistry(_time, access);
        _registry.Add(new SessionRecord(Sid, Oid, TestSessions.TenantId, T + TimeSpan.FromHours(8), T, T, "fp"));
        _accessor.User = TestPrincipal.From([new Claim("oid", Oid), new Claim(SessionClaims.Sid, Sid)]);
        _recent = new CountingRecentAuthentication(new RecentAuthenticationCheck(_accessor, _registry, _time, access));

        Context.Services.AddSingleton<TimeProvider>(_time);
        Context.Services.AddSingleton(access);
        Context.Services.AddSingleton(_registry);
        Context.Services.AddSingleton(_accessor);
        Context.Services.AddSingleton<IRecentAuthentication>(_recent);
        Context.Services.AddScoped<ActivityReporter>();
    }

    private IRenderedComponent<MudDialogProvider> Dialogs() => Render<MudDialogProvider>();

    // Advances the clock and waits until the watch has read the registry and rendered what it read.
    private async Task AdvanceAndReadAsync<TComponent>(TimeSpan by, IRenderedComponent<TComponent> any)
        where TComponent : IComponent
    {
        var before = _recent.Reads;
        _time.Advance(by);
        any.WaitForAssertion(() => Assert.True(_recent.Reads > before, "the registry is read"));
        await any.InvokeAsync(() => { });
    }

    private IReadOnlyList<NavigationHistory> Navigations() =>
        [.. ((BunitNavigationManager)Context.Services.GetRequiredService<NavigationManager>()).History];

    [Fact]
    public async Task Warning_appears_at_13_minutes_and_not_before()
    {
        var dialogs = Dialogs();
        var watch = Render<IdleWatch>();

        await AdvanceAndReadAsync(new TimeSpan(0, 12, 59), watch);
        Assert.DoesNotContain(Warning, dialogs.Markup, StringComparison.Ordinal);

        await AdvanceAndReadAsync(TimeSpan.FromSeconds(1), watch);
        dialogs.WaitForAssertion(() => Assert.Contains(Warning, dialogs.Markup, StringComparison.Ordinal));
        Assert.Contains("2:00", dialogs.Markup, StringComparison.Ordinal);
        Assert.Contains(Context.JSInterop.Invocations, i => i.Identifier == "poSession.warning" && Equals(i.Arguments[0], true));
    }

    [Fact]
    public async Task Warning_hides_when_another_tab_renews()
    {
        var dialogs = Dialogs();
        var watch = Render<IdleWatch>();
        await AdvanceAndReadAsync(TimeSpan.FromMinutes(13), watch);
        dialogs.WaitForAssertion(() => Assert.Contains(Warning, dialogs.Markup, StringComparison.Ordinal));

        Assert.True(_registry.Touch(Sid, Oid));
        await AdvanceAndReadAsync(TimeSpan.FromSeconds(15), watch);

        dialogs.WaitForAssertion(() => Assert.DoesNotContain(Warning, dialogs.Markup, StringComparison.Ordinal));
        Assert.Contains(Context.JSInterop.Invocations, i => i.Identifier == "poSession.warning" && Equals(i.Arguments[0], false));
    }

    [Fact]
    public void Expiry_navigates_to_signed_out_inactive()
    {
        var first = Render<IdleWatch>();
        Render<IdleWatch>();

        _time.Advance(TimeSpan.FromMinutes(15));

        first.WaitForAssertion(() => Assert.Equal(2, Navigations().Count));
        Assert.All(Navigations(), n =>
        {
            Assert.EndsWith("/signed-out?reason=inactive", n.Uri, StringComparison.Ordinal);
            Assert.True(n.Options.ForceLoad);
        });
    }

    [Fact]
    public async Task Stay_signed_in_reports_activity()
    {
        var dialogs = Dialogs();
        var watch = Render<IdleWatch>();
        await AdvanceAndReadAsync(TimeSpan.FromMinutes(13), watch);
        dialogs.WaitForAssertion(() => Assert.Contains(Warning, dialogs.Markup, StringComparison.Ordinal));

        dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Stay signed in").Click();

        Assert.Equal(_time.GetUtcNow(), _registry.Find(Sid)!.LastActivity);
        dialogs.WaitForAssertion(() => Assert.DoesNotContain(Warning, dialogs.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Report_activity_is_refused_after_expiry()
    {
        var reporter = new ActivityReporter(_accessor, _registry);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(reporter.ReportActivity());
        Assert.Equal(_time.GetUtcNow(), _registry.Find(Sid)!.LastActivity);

        _time.Advance(TimeSpan.FromMinutes(15));

        Assert.False(reporter.ReportActivity());
        Assert.False(_registry.Status(Sid, Oid).IsLive);
    }

    [Fact]
    public async Task Disposal_stops_timer_listeners_and_popup_polling()
    {
        var watch = Render<IdleWatch>();
        Render<ConfirmIdentity>(p => p.Add(c => c.Action, "approve the ticket"));
        watch.WaitForAssertion(() => Assert.Contains(Context.JSInterop.Invocations, i => i.Identifier == "poSession.register"));

        await Context.DisposeComponentsAsync();
        var reads = _recent.Reads;
        _time.Advance(TimeSpan.FromMinutes(20));
        await Task.Delay(100);

        Assert.Equal(reads, _recent.Reads);
        Assert.Single(Context.JSInterop.Invocations, i => i.Identifier == "poSession.stop");
        Assert.Single(Context.JSInterop.Invocations, i => i.Identifier == "poSession.release");
        Assert.Empty(Navigations());
    }

    [Fact]
    public async Task Repeated_navigation_registers_one_listener_set()
    {
        for (var i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                await Context.DisposeComponentsAsync();
            }

            var watch = Render<IdleWatch>();
            watch.WaitForAssertion(() => Assert.Equal(i + 1, Context.JSInterop.Invocations.Count(x => x.Identifier == "poSession.start")));
        }

        var references = Context.JSInterop.Invocations.Where(i => i.Identifier == "poSession.start")
            .Select(i => Assert.IsType<DotNetObjectReference<ActivityReporter>>(i.Arguments[0])).ToList();
        Assert.Equal(2, Context.JSInterop.Invocations.Count(i => i.Identifier == "poSession.stop"));
        Assert.Equal(1, references.Count(IsLive));
    }

    // A disposed reference refuses access to its value.
    private static bool IsLive(DotNetObjectReference<ActivityReporter> reference)
    {
        try
        {
            _ = reference.Value;
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>The real check, counting how often the registry is read through it.</summary>
    private sealed class CountingRecentAuthentication(IRecentAuthentication inner) : IRecentAuthentication
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public bool IsFresh
        {
            get
            {
                Interlocked.Increment(ref _reads);
                return inner.IsFresh;
            }
        }

        public SessionStatus Status
        {
            get
            {
                Interlocked.Increment(ref _reads);
                return inner.Status;
            }
        }
    }
}
