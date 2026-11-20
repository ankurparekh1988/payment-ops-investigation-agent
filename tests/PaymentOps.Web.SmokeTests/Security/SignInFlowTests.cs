using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

/// <summary>Sign-in and step-up through the real OpenID Connect callback and cookie sign-in.</summary>
public sealed partial class SignInFlowTests
{
    private const string Flow = "0123456789abcdef0123456789abcdef";
    private const string AuthCookie = ".AspNetCore.Cookies";
    private const string PreviousCookie = "paymentops.previous";
    private const string UserA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string UserB = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string OtherTenant = "99999999-9999-9999-9999-999999999999";

    [Fact]
    public async Task Sign_in_challenge_sends_prompt_login()
    {
        using var app = new OidcCallbackFactory();
        using var client = app.Client();

        using var challenge = await client.GetAsync(Relative("/"));

        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var signIn = Authorize(challenge);
        Assert.Equal("login", signIn["prompt"]);
        Assert.Equal(SignInFlow.SignInPurpose, StateOf(app, signIn).Items[SignInFlow.PurposeKey]);

        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        using var stepUp = await ReauthenticateAsync(app, jar, Flow);

        Assert.Equal(HttpStatusCode.Redirect, stepUp.StatusCode);
        var query = Authorize(stepUp);
        Assert.Equal("login", query["prompt"]);
        Assert.Equal("user@contoso.example", query["login_hint"]);
        var state = StateOf(app, query);
        Assert.Equal(SignInFlow.StepUpPurpose, state.Items[SignInFlow.PurposeKey]);
        Assert.Equal(sid, state.Items[SignInFlow.SidKey]);
        Assert.Equal(UserA, state.Items[SignInFlow.OidKey]);
        Assert.Equal(OidcCallbackFactory.TenantId, state.Items[SignInFlow.TidKey]);
        Assert.Equal(Flow, state.Items[SignInFlow.FlowKey]);
        Assert.Equal($"/account/confirm-result?flow={Flow}&result=confirmed", state.RedirectUri);
    }

    [Theory]
    [InlineData("//evil", "/")]
    [InlineData("/\\evil", "/")]
    [InlineData("https://x", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("/investigate", "/investigate")]
    public void Return_urls_stay_local(string? url, string expected) =>
        Assert.Equal(expected, SignInFlow.LocalReturnUrl(url));

    [Fact]
    public async Task Reauthenticate_without_antiforgery_is_rejected()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var token = await AntiforgeryTokenAsync(app, jar);

        using var withoutToken = await jar.SendAsync(app.Client(), HttpMethod.Post, "/account/reauthenticate", Form(("flow", Flow)));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Null(withoutToken.Headers.Location);

        // A page loaded before the session ended still holds a token; the popup must learn it is signed out.
        app.Registry.Remove(sid);
        using var signedOut = await jar.SendAsync(
            app.Client(), HttpMethod.Post, "/account/reauthenticate", Form(("__RequestVerificationToken", token), ("flow", Flow)));
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Null(signedOut.Headers.Location);
    }

    [Fact]
    public async Task Normal_callback_sets_one_cookie_and_one_record()
    {
        using var app = new OidcCallbackFactory();
        var now = app.Time.GetUtcNow();
        var token = app.IdToken(UserA, roles: [OpsRoles.Admin], groups: [PaymentOpsWebApplicationFactory.RestrictedGroupId], authTime: app.AuthTime(TimeSpan.FromSeconds(-30)));

        using var response = await app.CallbackAsync(SignInItems(), token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Single(CookieJar.SetCookies(response), c => c.Name.StartsWith(AuthCookie, StringComparison.Ordinal) && !c.Deleted);
        Assert.Equal(1, app.Registry.Count);

        var jar = new CookieJar().Apply(response);
        var ticket = app.Ticket(jar)!;
        var sid = SessionClaims.SidOf(ticket.Principal)!;
        Assert.Matches("^[0-9a-f]{32}$", sid);
        var record = app.Registry.Find(sid)!;
        Assert.Equal(UserA, record.UserId);
        Assert.Equal(OidcCallbackFactory.TenantId, record.TenantId);
        Assert.Equal(now - TimeSpan.FromSeconds(30), record.AuthenticatedAt);
        Assert.Equal(now + TimeSpan.FromHours(8), record.AbsoluteExpiry);
        Assert.Equal(now, record.LastActivity);
        Assert.Equal(RecentAuthentication.Fingerprint(OpsRoles.Effective([OpsRoles.Admin]), [PaymentOpsWebApplicationFactory.RestrictedGroupId]), record.AccessFingerprint);

        Assert.Equal(record.AbsoluteExpiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ticket.Principal.FindFirst(SignInSession.ExpiresClaim)?.Value);
        Assert.Equal(record.AbsoluteExpiry, ticket.Properties.ExpiresUtc);
        Assert.False(ticket.Properties.AllowRefresh);
        Assert.DoesNotContain(ticket.Properties.Items.Keys, key => key.StartsWith("paymentops.", StringComparison.Ordinal));

        using var home = await jar.SendAsync(app.Client(), HttpMethod.Get, "/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Theory]
    [InlineData("missing oid")]
    [InlineData("cancelled")]
    public async Task Failed_sign_in_goes_to_the_signed_out_page(string failure)
    {
        using var app = new OidcCallbackFactory();

        using var response = failure == "cancelled"
            ? await app.ErrorCallbackAsync(SignInItems(), "access_denied")
            : await app.CallbackAsync(SignInItems(), app.IdToken(string.Empty, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)));

        Assert.Equal("/signed-out?reason=sign-in-failed", response.Headers.Location?.OriginalString);
        AssertNoSignInCookie(response);
        Assert.Equal(0, app.Registry.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("future")]
    public async Task Normal_callback_without_auth_time_starts_a_session_that_is_not_fresh(string? authTime)
    {
        using var app = new OidcCallbackFactory();
        var claim = authTime == "future" ? app.AuthTime(TimeSpan.FromMinutes(3)) : authTime;

        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin], authTime: claim);

        Assert.Null(app.Registry.Find(sid)!.AuthenticatedAt);
        using var home = await jar.SendAsync(app.Client(), HttpMethod.Get, "/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Fact]
    public async Task Step_up_keeps_sid_and_original_expiry()
    {
        using var app = new OidcCallbackFactory();
        var signedInAt = app.Time.GetUtcNow();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        StayActive(app, sid, UserA, TimeSpan.FromHours(7));
        var steppedUpAt = app.Time.GetUtcNow();

        using var response = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)));

        Assert.Equal($"/account/confirm-result?flow={Flow}&result=confirmed", response.Headers.Location?.OriginalString);
        var ticket = app.Ticket(jar.Apply(response))!;
        Assert.Equal(sid, SessionClaims.SidOf(ticket.Principal));
        Assert.Equal(signedInAt + TimeSpan.FromHours(8), ticket.Properties.ExpiresUtc);
        Assert.Equal((signedInAt + TimeSpan.FromHours(8)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ticket.Principal.FindFirst(SignInSession.ExpiresClaim)?.Value);
        Assert.False(ticket.Properties.AllowRefresh);
        var record = app.Registry.Find(sid)!;
        Assert.Equal(steppedUpAt, record.AuthenticatedAt);
        Assert.Equal(signedInAt + TimeSpan.FromHours(8), record.AbsoluteExpiry);
        Assert.Equal(1, app.Registry.Count);
    }

    [Theory]
    [InlineData("oid")]
    [InlineData("tid")]
    public async Task Step_up_by_a_different_account_issues_no_cookie_and_ends_the_session(string differs)
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var token = differs == "oid"
            ? app.IdToken(UserB, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero))
            : app.IdToken(UserA, tid: OtherTenant, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero));

        using var response = await StepUpAsync(app, jar, sid, UserA, token);

        AssertNoSignInCookie(response);
        Assert.Null(app.Registry.Find(sid));
        Assert.Equal("different-account", ResultOf(response));
    }

    [Fact]
    public async Task Step_up_after_role_removed_ends_the_session()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var oldTab = jar.Copy();

        using var response = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Reader], authTime: app.AuthTime(TimeSpan.Zero)));

        AssertNoSignInCookie(response);
        Assert.Equal("access-changed", ResultOf(response));
        Assert.Null(app.Registry.Find(sid));
        using var old = await oldTab.SendAsync(app.Client(), HttpMethod.Get, "/");
        AssertChallenged(old);
    }

    [Fact]
    public async Task Step_up_after_restricted_group_removed_ends_the_session()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Reader], [PaymentOpsWebApplicationFactory.RestrictedGroupId]);
        var oldTab = jar.Copy();

        using var response = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Reader], authTime: app.AuthTime(TimeSpan.Zero)));

        AssertNoSignInCookie(response);
        Assert.Equal("access-changed", ResultOf(response));
        Assert.Null(app.Registry.Find(sid));
        using var old = await oldTab.SendAsync(app.Client(), HttpMethod.Get, "/");
        AssertChallenged(old);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("abc")]
    [InlineData("too-old")]
    [InlineData("future")]
    public async Task Step_up_with_invalid_auth_time_changes_nothing(string authTime)
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        StayActive(app, sid, UserA, TimeSpan.FromMinutes(10));
        var before = app.Registry.Find(sid)!;
        var claim = authTime switch
        {
            "missing" => null,
            "abc" => "abc",
            "too-old" => app.AuthTime(-TimeSpan.FromSeconds(301)),
            _ => app.AuthTime(TimeSpan.FromSeconds(121)),
        };

        using var response = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: claim));

        AssertNoSignInCookie(response);
        Assert.Equal("not-confirmed", ResultOf(response));
        Assert.Equal(before, app.Registry.Find(sid));
        Assert.True(app.Registry.Status(sid, UserA).IsLive);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("absolute")]
    public async Task Step_up_on_expired_session_fails(string expiry)
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        if (expiry == "idle")
        {
            app.Time.Advance(TimeSpan.FromMinutes(15));
        }
        else
        {
            StayActive(app, sid, UserA, TimeSpan.FromHours(8));
        }

        Assert.False(app.Registry.Status(sid, UserA).IsLive);
        var before = app.Registry.Find(sid);

        using var response = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)));

        AssertNoSignInCookie(response);
        Assert.Equal("ended", ResultOf(response));
        Assert.Equal(before, app.Registry.Find(sid));
    }

    [Fact]
    public async Task Cancelled_step_up_changes_nothing()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var before = app.Registry.Find(sid);

        using var response = await app.ErrorCallbackAsync(StepUpItems(sid, UserA), "access_denied", jar);

        Assert.Equal($"/account/confirm-result?flow={Flow}&result=cancelled", response.Headers.Location?.OriginalString);
        AssertNoSignInCookie(response);
        Assert.Equal(before, app.Registry.Find(sid));
    }

    [Fact]
    public async Task Commit_happens_only_after_the_cookie_is_written()
    {
        using var app = new OidcCallbackFactory();
        var (signedIn, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        app.Time.Advance(TimeSpan.FromMinutes(1));
        var before = app.Registry.Find(sid);
        app.OnCookieSigningIn = () => throw new InvalidOperationException("The cookie could not be written.");

        var jar = new CookieJar();
        await SendIgnoringServerErrorAsync(() => app.CallbackAsync(SignInItems(), app.IdToken(UserB, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero))), jar);
        await SendIgnoringServerErrorAsync(() => StepUpAsync(app, signedIn.Copy(), sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero))), new CookieJar());

        Assert.Equal(1, app.Registry.Count);
        Assert.Equal(before, app.Registry.Find(sid));
        app.OnCookieSigningIn = null;
        using var next = await jar.SendAsync(app.Client(), HttpMethod.Get, "/");
        AssertChallenged(next);
    }

    [Theory]
    [InlineData("sign-in")]
    [InlineData("step-up")]
    public async Task Commit_rechecks_liveness(string purpose)
    {
        using var app = new OidcCallbackFactory();
        if (purpose == "sign-in")
        {
            app.BeforeCookieSignIn = () => app.Time.Advance(TimeSpan.FromMinutes(15));

            using var response = await app.CallbackAsync(SignInItems(), app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(0, app.Registry.Count);
            return;
        }

        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.FromMinutes(-20)));
        app.Time.Advance(TimeSpan.FromMinutes(1));
        var before = app.Registry.Find(sid);
        app.BeforeCookieSignIn = () => app.Time.Advance(TimeSpan.FromMinutes(15));

        using var stepUp = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)));

        Assert.Equal(before, app.Registry.Find(sid));
    }

    [Fact]
    public async Task Another_account_signing_in_ends_the_previous_session()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sidA) = await SignInAsync(app, UserA, [OpsRoles.Admin]);

        var (_, sidB) = await SignInAsync(app, UserB, [OpsRoles.Reader], jar: jar);

        Assert.Null(app.Registry.Find(sidA));
        Assert.Equal(UserB, app.Registry.Find(sidB)!.UserId);
        Assert.Equal(1, app.Registry.Count);
    }

    [Fact]
    public async Task Same_user_signing_in_again_replaces_the_session()
    {
        using var app = new OidcCallbackFactory();
        var (jar, first) = await SignInAsync(app, UserA, [OpsRoles.Admin]);

        var (_, second) = await SignInAsync(app, UserA, [OpsRoles.Admin], jar: jar);

        Assert.NotEqual(first, second);
        Assert.Null(app.Registry.Find(first));
        Assert.NotNull(app.Registry.Find(second));
        Assert.Equal(1, app.Registry.Count);
    }

    [Fact]
    public async Task Tampered_previous_cookie_is_ignored()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sidA) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        jar.Set(PreviousCookie, "not-a-protected-value", crossSite: true);

        var (_, sidB) = await SignInAsync(app, UserB, [OpsRoles.Reader], jar: jar);

        Assert.NotNull(app.Registry.Find(sidA));
        Assert.NotNull(app.Registry.Find(sidB));
        Assert.Equal(2, app.Registry.Count);
    }

    [Fact]
    public async Task Sign_out_clears_the_previous_session_cookie()
    {
        using var app = new OidcCallbackFactory();
        var jar = new CookieJar();
        using (var signIn = await app.CallbackAsync(SignInItems(), app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero))))
        {
            var header = Assert.Single(signIn.Headers.GetValues("Set-Cookie"), h => h.StartsWith(PreviousCookie + "=", StringComparison.Ordinal));
            Assert.Contains("samesite=none", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
            jar.Apply(signIn);
        }

        Assert.NotNull(jar.Get(PreviousCookie));
        var token = await AntiforgeryTokenAsync(app, jar);

        using var signOut = await jar.SendAsync(app.Client(), HttpMethod.Post, "/account/sign-out", Form(("__RequestVerificationToken", token)));

        Assert.Contains(CookieJar.SetCookies(signOut), c => c.Name == PreviousCookie && c.Deleted);
        Assert.Null(jar.Get(PreviousCookie));
    }

    [Fact]
    public async Task Popup_message_is_not_authority()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.FromMinutes(-20)));
        var before = app.Registry.Find(sid)!.AuthenticatedAt;

        using var page = await jar.SendAsync(app.Client(), HttpMethod.Get, $"/account/confirm-result?flow={Flow}&result=confirmed");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(before, app.Registry.Find(sid)!.AuthenticatedAt);
    }

    [Fact]
    public async Task Flow_round_trips_through_every_outcome()
    {
        using var app = new OidcCallbackFactory();
        var fresh = app.AuthTime(TimeSpan.Zero);

        async Task<string> OutcomeAsync(Func<CookieJar, string, Task<HttpResponseMessage>> stepUp)
        {
            var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
            using var response = await stepUp(jar, sid);
            var query = QueryHelpers.ParseQuery(response.Headers.Location!.OriginalString.Split('?')[1]);
            Assert.Equal(Flow, query["flow"].ToString());
            return query["result"].ToString();
        }

        Assert.Equal("confirmed", await OutcomeAsync((jar, sid) => StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: fresh))));
        Assert.Equal("ended", await OutcomeAsync((jar, sid) =>
        {
            app.Registry.Remove(sid);
            return StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: fresh));
        }));
        Assert.Equal("different-account", await OutcomeAsync((jar, sid) => StepUpAsync(app, jar, sid, UserA, app.IdToken(UserB, roles: [OpsRoles.Admin], authTime: fresh))));
        Assert.Equal("access-changed", await OutcomeAsync((jar, sid) => StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Engineer], authTime: fresh))));
        Assert.Equal("not-confirmed", await OutcomeAsync((jar, sid) => StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin]))));
        Assert.Equal("cancelled", await OutcomeAsync((jar, sid) => app.ErrorCallbackAsync(StepUpItems(sid, UserA), "access_denied", jar)));
        Assert.Equal("not-confirmed", await OutcomeAsync((jar, sid) => app.ErrorCallbackAsync(StepUpItems(sid, UserA), "server_error", jar)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdef\n")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Reauthenticate_rejects_a_malformed_flow(string flow)
    {
        using var app = new OidcCallbackFactory();
        var (jar, _) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var token = await AntiforgeryTokenAsync(app, jar);

        using var response = await jar.SendAsync(
            app.Client(), HttpMethod.Post, "/account/reauthenticate", Form(("__RequestVerificationToken", token), ("flow", flow)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("missing")]
    public async Task Tampered_state_redirects_without_a_flow(string state)
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var before = app.Registry.Find(sid);
        var valid = app.State(StepUpItems(sid, UserA), "unused");
        var sent = state == "missing" ? string.Empty : valid[..^4] + (valid[^4] == 'A' ? 'B' : 'A') + valid[^3..];

        using var response = await app.CallbackAsync(
            StepUpItems(sid, UserA), app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)), jar, stateOverride: sent);

        Assert.Equal("/account/confirm-result?result=not-confirmed", response.Headers.Location?.OriginalString);
        AssertNoSignInCookie(response);
        Assert.Equal(before, app.Registry.Find(sid));
    }

    [Fact]
    public async Task Confirm_result_page_renders_only_a_well_formed_flow()
    {
        using var app = new OidcCallbackFactory();
        using var client = app.Client();

        var rejected = await client.GetStringAsync(Relative("/account/confirm-result?flow=%3Cscript%3E&result=bogus"));
        var accepted = await client.GetStringAsync(Relative($"/account/confirm-result?flow={Flow}&result=cancelled"));
        var confirmed = await client.GetStringAsync(Relative($"/account/confirm-result?flow={Flow}&result=confirmed"));

        Assert.Contains("id=\"confirm-result\" data-flow=\"\" data-result=\"not-confirmed\"", rejected, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", rejected.Replace("<script src=", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains($"id=\"confirm-result\" data-flow=\"{Flow}\" data-result=\"cancelled\"", accepted, StringComparison.Ordinal);
        Assert.Contains("Confirmed. Return to the other tab and choose the action again.", confirmed, StringComparison.Ordinal);
        Assert.Contains("js/confirm-result", confirmed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_step_ups_on_one_session_both_commit()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.FromMinutes(-20)));
        StayActive(app, sid, UserA, TimeSpan.FromMinutes(2));
        var later = app.Time.GetUtcNow() - TimeSpan.FromSeconds(30);

        var responses = await Task.WhenAll(
            StepUpAsync(app, jar.Copy(), sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.FromSeconds(-30)))),
            StepUpAsync(app, jar.Copy(), sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.FromMinutes(-1)))));

        Assert.All(responses, response => Assert.Equal("confirmed", ResultOf(response)));
        Assert.Equal(later, app.Registry.Find(sid)!.AuthenticatedAt);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Replaying_the_cookie_after_sign_out_fails()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin]);
        var copy = jar.Copy();
        var token = await AntiforgeryTokenAsync(app, jar);

        using var signOut = await jar.SendAsync(app.Client(), HttpMethod.Post, "/account/sign-out", Form(("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Null(app.Registry.Find(sid));
        Assert.Equal(0, app.Registry.Count);
        using var replay = await copy.SendAsync(app.Client(), HttpMethod.Get, "/");
        AssertChallenged(replay);
    }

    [Fact]
    public async Task Step_up_racing_sign_out_records_nothing()
    {
        using var app = new OidcCallbackFactory();
        var (jar, sid) = await SignInAsync(app, UserA, [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.FromMinutes(-20)));
        app.BeforeCookieSignIn = () => app.Registry.Remove(sid);

        using var response = await StepUpAsync(app, jar, sid, UserA, app.IdToken(UserA, roles: [OpsRoles.Admin], authTime: app.AuthTime(TimeSpan.Zero)));

        Assert.Null(app.Registry.Find(sid));
        Assert.Equal(0, app.Registry.Count);
        using var next = await jar.Apply(response).SendAsync(app.Client(), HttpMethod.Get, "/");
        AssertChallenged(next);
    }

    private static Dictionary<string, string?> SignInItems() => new() { [SignInFlow.PurposeKey] = SignInFlow.SignInPurpose };

    private static Dictionary<string, string?> StepUpItems(string sid, string oid) => new()
    {
        [SignInFlow.PurposeKey] = SignInFlow.StepUpPurpose,
        [SignInFlow.SidKey] = sid,
        [SignInFlow.OidKey] = oid,
        [SignInFlow.TidKey] = OidcCallbackFactory.TenantId,
        [SignInFlow.FlowKey] = Flow,
    };

    private static async Task<(CookieJar Jar, string Sid)> SignInAsync(
        OidcCallbackFactory app, string oid, string[] roles, string[]? groups = null, string? authTime = "now", CookieJar? jar = null)
    {
        jar ??= new CookieJar();
        var claim = authTime == "now" ? app.AuthTime(TimeSpan.Zero) : authTime;
        using var response = await app.CallbackAsync(SignInItems(), app.IdToken(oid, roles: roles, groups: groups, authTime: claim), jar);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        jar.Apply(response);
        var sid = SessionClaims.SidOf(app.Ticket(jar)!.Principal);
        Assert.NotNull(sid);
        return (jar, sid);
    }

    private static Task<HttpResponseMessage> StepUpAsync(OidcCallbackFactory app, CookieJar jar, string sid, string oid, string idToken) =>
        app.CallbackAsync(StepUpItems(sid, oid), idToken, jar);

    private static async Task<HttpResponseMessage> ReauthenticateAsync(OidcCallbackFactory app, CookieJar jar, string flow)
    {
        var token = await AntiforgeryTokenAsync(app, jar);
        return await jar.SendAsync(app.Client(), HttpMethod.Post, "/account/reauthenticate", Form(("__RequestVerificationToken", token), ("flow", flow)));
    }

    private static async Task SendIgnoringServerErrorAsync(Func<Task<HttpResponseMessage>> send, CookieJar jar)
    {
        try
        {
            using var response = await send();
            jar.Apply(response);
        }
        catch (InvalidOperationException)
        {
            // The test server may rethrow the failed sign-in instead of answering 500; either way nothing was committed.
        }
    }

    // Activity keeps a session alive through long periods; the absolute expiry still applies.
    private static void StayActive(OidcCallbackFactory app, string sid, string oid, TimeSpan duration)
    {
        var end = app.Time.GetUtcNow() + duration;
        while (app.Time.GetUtcNow() < end)
        {
            var step = end - app.Time.GetUtcNow() < TimeSpan.FromMinutes(10) ? end - app.Time.GetUtcNow() : TimeSpan.FromMinutes(10);
            app.Time.Advance(step);
            app.Registry.Touch(sid, oid);
        }
    }

    private static async Task<string> AntiforgeryTokenAsync(OidcCallbackFactory app, CookieJar jar)
    {
        using var page = await jar.SendAsync(app.Client(), HttpMethod.Get, "/access-denied");
        var html = await page.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(AntiforgeryField().Match(html).Groups["token"].Value);
        Assert.NotEmpty(token);
        return token;
    }

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));

    private static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Authorize(HttpResponseMessage response)
    {
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith(OidcCallbackFactory.AuthorizeEndpoint, location, StringComparison.Ordinal);
        return QueryHelpers.ParseQuery(new Uri(location).Query);
    }

    private static AuthenticationProperties StateOf(OidcCallbackFactory app, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query) =>
        app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme)
            .StateDataFormat.Unprotect(query["state"])!;

    private static string? ResultOf(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.OriginalString;
        Assert.NotNull(location);
        Assert.StartsWith("/account/confirm-result?", location, StringComparison.Ordinal);
        return QueryHelpers.ParseQuery(location.Split('?')[1])["result"];
    }

    private static void AssertNoSignInCookie(HttpResponseMessage response) =>
        Assert.DoesNotContain(CookieJar.SetCookies(response), c => c.Name.StartsWith(AuthCookie, StringComparison.Ordinal) && !c.Deleted);

    private static void AssertChallenged(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(OidcCallbackFactory.AuthorizeEndpoint, response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
