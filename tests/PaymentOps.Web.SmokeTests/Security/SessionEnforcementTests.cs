using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

public sealed partial class SessionEnforcementTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private const string Oid = "user-1";
    private const string Sid = "session-1";

    private readonly FakeTimeProvider _time = new(T);

    [Theory]
    [InlineData("no record")]
    [InlineData("idle-expired")]
    [InlineData("absolute-expired")]
    public void Expired_session_reads_as_signed_out_even_with_roles_and_groups(string state)
    {
        var registry = Registry();
        switch (state)
        {
            case "idle-expired":
                registry.Add(Record());
                _time.Advance(TimeSpan.FromMinutes(15));
                break;
            case "absolute-expired":
                registry.Add(Record() with { AbsoluteExpiry = T + TimeSpan.FromMinutes(10) });
                _time.Advance(TimeSpan.FromMinutes(9));
                Assert.True(registry.Touch(Sid, Oid));
                _time.Advance(TimeSpan.FromMinutes(2));
                break;
        }

        var user = UserContext(registry);

        Assert.False(user.IsAuthenticated);
        Assert.Equal(string.Empty, user.UserId);
        Assert.Equal(string.Empty, user.DisplayName);
        Assert.False(user.IsInRole(OpsRoles.Admin));
        Assert.False(user.CanAccessRestrictedKnowledge);
        Assert.Empty(user.Roles);
        Assert.Empty(user.GroupIds);
    }

    [Fact]
    public void Live_session_reads_roles_and_groups()
    {
        var registry = Registry();
        registry.Add(Record());

        var user = UserContext(registry);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(Oid, user.UserId);
        Assert.Equal("Avery Admin", user.DisplayName);
        Assert.True(user.IsInRole(OpsRoles.Admin));
        Assert.True(user.CanAccessRestrictedKnowledge);
        Assert.Contains(PaymentOpsWebApplicationFactory.RestrictedGroupId, user.GroupIds);

        // Evaluated on every call: the same context reads as signed out once the session idles out.
        _time.Advance(TimeSpan.FromMinutes(15));
        Assert.False(user.IsAuthenticated);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public void Recent_authentication_reads_the_live_session()
    {
        var registry = Registry();
        registry.Add(Record());
        var check = new RecentAuthenticationCheck(new CurrentUserAccessor { User = Principal() }, registry, _time, Options.Create(new AccessOptions()));

        Assert.True(check.Status.IsLive);
        Assert.True(check.IsFresh);

        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.True(registry.Touch(Sid, Oid));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(check.Status.IsLive);
        Assert.False(check.IsFresh);

        registry.Remove(Sid);
        Assert.False(check.Status.IsLive);
        Assert.False(check.IsFresh);
    }

    [Fact]
    public async Task Passive_requests_do_not_touch()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, _) = PaymentOpsWebApplicationFactory.SessionOf(client);
        _time.Advance(TimeSpan.FromMinutes(1));

        using (await client.GetAsync(Relative("/health"))) { }
        using (await client.GetAsync(Relative("/_framework/blazor.web.js"))) { }
        using (await client.GetAsync(Relative("/app.css"))) { }
        using (await client.PostAsync(Relative("/_blazor/negotiate?negotiateVersion=1"), content: null)) { }

        Assert.Equal(T, Registry(app).Find(sid)!.LastActivity);
    }

    [Fact]
    public async Task Page_request_touches()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, _) = PaymentOpsWebApplicationFactory.SessionOf(client);
        _time.Advance(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync(Relative("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(T + TimeSpan.FromSeconds(10), Registry(app).Find(sid)!.LastActivity);
    }

    // The premise behind the circuit's check: activity lands at submit, and idle (15 min) outlasts the longest turn (60 s).
    [Fact]
    public async Task Submitting_renews_so_idle_cannot_fall_inside_a_turn()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, oid) = PaymentOpsWebApplicationFactory.SessionOf(client);
        _time.Advance(TimeSpan.FromMinutes(14));

        using (await client.GetAsync(Relative("/investigate"))) { }
        var submitted = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(submitted, Registry(app).Find(sid)!.LastActivity);
        Assert.True(Registry(app).Status(sid, oid).IsLive);
    }

    [Fact]
    public async Task Stale_cookie_after_expiry_is_signed_out()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);

        // Control: the same request succeeds while the session is live, so the refusal below is the expiry's doing.
        using (var liveNegotiate = await client.PostAsync(Relative("/_blazor/negotiate?negotiateVersion=1"), content: null))
        {
            Assert.Equal(HttpStatusCode.OK, liveNegotiate.StatusCode);
        }

        _time.Advance(TimeSpan.FromMinutes(15));

        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var page = await client.GetAsync(Relative("/"));
        using var negotiate = await client.PostAsync(Relative("/_blazor/negotiate?negotiateVersion=1"), content: null);
        using var anonymousNegotiate = await anonymous.PostAsync(Relative("/_blazor/negotiate?negotiateVersion=1"), content: null);

        AssertChallenged(page);
        // The 401 is re-executed as the not-found page, which turns it into a 400; either way no circuit is offered.
        Assert.False(negotiate.IsSuccessStatusCode);
        Assert.Equal(anonymousNegotiate.StatusCode, negotiate.StatusCode);
    }

    [Fact]
    public async Task Restart_rejects_an_unknown_session()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, _) = PaymentOpsWebApplicationFactory.SessionOf(client);
        Registry(app).Remove(sid);

        using var response = await client.GetAsync(Relative("/"));

        AssertChallenged(response);
        Assert.Null(Registry(app).Find(sid));
    }

    [Fact]
    public async Task Sign_out_from_a_page_loaded_before_expiry_still_signs_out()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, _) = PaymentOpsWebApplicationFactory.SessionOf(client);
        var token = await AntiforgeryTokenFrom(client);
        Assert.NotEmpty(token);
        _time.Advance(TimeSpan.FromMinutes(15));

        using var response = await SignOut(client, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signed-out", response.Headers.Location?.OriginalString);
        Assert.Null(Registry(app).Find(sid));
    }

    [Fact]
    public async Task Sign_out_revokes_the_session()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, _) = PaymentOpsWebApplicationFactory.SessionOf(client);
        var token = await AntiforgeryTokenFrom(client);

        using var signOut = await SignOut(client, token);

        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Null(Registry(app).Find(sid));
        using var replay = await client.GetAsync(Relative("/"));
        AssertChallenged(replay);
    }

    [Fact]
    public async Task Live_session_sign_out_without_antiforgery_is_rejected()
    {
        using var app = AppWithClock();
        using var client = SignedIn(app);
        var (sid, _) = PaymentOpsWebApplicationFactory.SessionOf(client);

        using var signOut = await client.PostAsync(Relative("/account/sign-out"), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        Assert.NotNull(Registry(app).Find(sid));
        using var next = await client.GetAsync(Relative("/"));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Each_client_keeps_one_session()
    {
        using var app = AppWithClock();
        using var first = SignedIn(app);
        using var second = SignedIn(app);
        var (sid, oid) = PaymentOpsWebApplicationFactory.SessionOf(first);
        var other = PaymentOpsWebApplicationFactory.SessionOf(second);

        _time.Advance(TimeSpan.FromSeconds(10));
        using (await first.GetAsync(Relative("/"))) { }
        _time.Advance(TimeSpan.FromSeconds(10));
        using (await first.GetAsync(Relative("/"))) { }

        var record = Registry(app).Find(sid)!;
        Assert.Equal(oid, record.UserId);
        Assert.Equal(T + TimeSpan.FromSeconds(20), record.LastActivity);
        Assert.Equal(T, Registry(app).Find(other.Sid)!.LastActivity);
        Assert.NotEqual(sid, other.Sid);
        Assert.NotEqual(oid, other.Oid);
    }

    private SessionRegistry Registry() => new(_time, Options.Create(new AccessOptions()));

    private static SessionRegistry Registry(WebApplicationFactory<Program> app) => app.Services.GetRequiredService<SessionRegistry>();

    private static SessionRecord Record() => TestSessions.Started(Sid, Oid, T, new AccessOptions());

    private static ClaimsPrincipal Principal() => TestPrincipal.From(
    [
        new Claim("oid", Oid),
        new Claim("name", "Avery Admin"),
        new Claim(SessionClaims.Sid, Sid),
        new Claim("roles", OpsRoles.Admin),
        new Claim("groups", PaymentOpsWebApplicationFactory.RestrictedGroupId),
    ]);

    private static ClaimsUserContext UserContext(SessionRegistry registry) => new(
        new CurrentUserAccessor { User = Principal() },
        Options.Create(new AccessOptions { RestrictedGroupId = PaymentOpsWebApplicationFactory.RestrictedGroupId }),
        registry);

    // The OpenID Connect sign-out reads Entra's metadata; a fixed configuration keeps the test offline.
    private WebApplicationFactory<Program> AppWithClock() => factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
    {
        services.AddSingleton<TimeProvider>(_time);
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { EndSessionEndpoint = "https://login.example/logout" }));
    }));

    private static HttpClient SignedIn(WebApplicationFactory<Program> app) =>
        PaymentOpsWebApplicationFactory.SignedIn(app, "Riley Reader", [OpsRoles.Reader]);

    private static async Task<string> AntiforgeryTokenFrom(HttpClient client)
    {
        var html = await client.GetStringAsync(Relative("/access-denied"));
        return WebUtility.HtmlDecode(AntiforgeryField().Match(html).Groups["token"].Value);
    }

    private static Task<HttpResponseMessage> SignOut(HttpClient client, string token) =>
        client.PostAsync(Relative("/account/sign-out"), new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

    private static void AssertChallenged(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized || (response.StatusCode is HttpStatusCode.Redirect && response.Headers.Location is not null),
            $"Expected a challenge, got {(int)response.StatusCode}.");

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
