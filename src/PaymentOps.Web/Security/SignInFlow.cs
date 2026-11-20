using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

/// <summary>
/// Sign-in and step-up through Entra ID. The OpenID Connect callback only validates and stages a commit in the
/// protected properties; the registry is written once the cookie itself has been signed in.
/// </summary>
internal static partial class SignInFlow
{
    public const string PurposeKey = "paymentops.purpose";
    public const string SidKey = "paymentops.sid";
    public const string OidKey = "paymentops.oid";
    public const string TidKey = "paymentops.tid";
    public const string FlowKey = "paymentops.flow";
    public const string CommitKey = "paymentops.commit";
    public const string ReplacesKey = "paymentops.replaces";

    public const string SignInPurpose = "sign-in";
    public const string StepUpPurpose = "step-up";

    /// <summary>Step-up outcomes, as the confirm-result page and its opener know them.</summary>
    public static class StepUpResults
    {
        public const string Confirmed = "confirmed";
        public const string Cancelled = "cancelled";
        public const string NotConfirmed = "not-confirmed";
        public const string DifferentAccount = "different-account";
        public const string AccessChanged = "access-changed";
        public const string Ended = "ended";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            Confirmed, Cancelled, NotConfirmed, DifferentAccount, AccessChanged, Ended,
        };
    }

    private const string ItemPrefix = "paymentops.";
    private const string PendingCommitItem = "PaymentOps.PendingCommit";
    private const string GroupsClaim = "groups";
    private const string PreferredUsernameClaim = "preferred_username";
    private const string AccessDeniedError = "access_denied";
    private const string PreviousSessionCookie = "paymentops.previous";
    private const string PreviousSessionPurpose = "PaymentOps.Web.Security.PreviousSession";
    private const string SignInFailedUrl = "/signed-out?reason=sign-in-failed";

    public static bool IsFlow(string? flow) => flow is not null && FlowPattern().IsMatch(flow);

    /// <summary>Only a path on this site: a leading single <c>/</c> not followed by <c>/</c> or <c>\</c>, with no control characters.</summary>
    public static string LocalReturnUrl(string? url) =>
        url is ['/'] or ['/', not ('/' or '\\'), ..] && !url.Any(char.IsControl) ? url : "/";

    public static string ConfirmResultUrl(string? flow, string result) =>
        IsFlow(flow) ? $"/account/confirm-result?flow={flow}&result={result}" : $"/account/confirm-result?result={result}";

    public static void Configure(OpenIdConnectOptions options, AccessOptions access)
    {
        var redirectToIdentityProvider = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            await redirectToIdentityProvider(context);

            // Single sign-on alone never starts a session or confirms one: Entra must ask for a credential.
            context.ProtocolMessage.Prompt = OpenIdConnectPrompt.Login;
            context.Properties.RedirectUri = LocalReturnUrl(context.Properties.RedirectUri);
            if (context.Properties.GetString(PurposeKey) == StepUpPurpose)
            {
                context.ProtocolMessage.LoginHint = context.HttpContext.User.FindFirst(PreferredUsernameClaim)?.Value;
            }
            else
            {
                context.Properties.Items[PurposeKey] = SignInPurpose;
            }
        };

        var tokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            await tokenValidated(context);
            if (context.Result is not null)
            {
                return;
            }

            if (context.Properties!.GetString(PurposeKey) == StepUpPurpose)
            {
                ValidateStepUp(context);
            }
            else
            {
                ValidateSignIn(context, access);
            }
        };

        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();

            // A failure raised from OnTokenValidated arrives without the properties, so it carries what the redirect needs.
            var properties = context.Properties;
            var url = context.Failure switch
            {
                StepUpRefusedException refused => ConfirmResultUrl(refused.Flow, refused.Result),
                SignInRefusedException => SignInFailedUrl,

                // Unreadable state: no flow can be trusted, so no opener will accept this result.
                _ when properties is null => ConfirmResultUrl(null, StepUpResults.NotConfirmed),
                _ when properties.GetString(PurposeKey) == StepUpPurpose => ConfirmResultUrl(
                    properties.GetString(FlowKey),
                    context.Failure?.Data["error"] as string == AccessDeniedError ? StepUpResults.Cancelled : StepUpResults.NotConfirmed),
                _ => SignInFailedUrl,
            };
            context.Response.Redirect(url);
            return Task.CompletedTask;
        };
    }

    public static void Configure(CookieAuthenticationOptions options)
    {
        var signingIn = options.Events.OnSigningIn;
        options.Events.OnSigningIn = async context =>
        {
            await signingIn(context);

            // The staged commit reaches OnSignedIn without being written into the cookie.
            var pending = context.Properties.Items.Where(item => item.Key.StartsWith(ItemPrefix, StringComparison.Ordinal)).ToDictionary();
            foreach (var key in pending.Keys)
            {
                context.Properties.Items.Remove(key);
            }

            context.HttpContext.Items[PendingCommitItem] = pending;
        };

        var signedIn = options.Events.OnSignedIn;
        options.Events.OnSignedIn = async context =>
        {
            await signedIn(context);
            if (context.HttpContext.Items[PendingCommitItem] is Dictionary<string, string?> pending
                && pending.GetValueOrDefault(CommitKey) is { } commit)
            {
                Commit(context.HttpContext, pending, commit);
            }
        };
    }

    /// <summary>Starts a step-up in a popup: a fresh Entra sign-in for the same session, reported back by flow id.</summary>
    public static async Task<IResult> ReauthenticateAsync(HttpContext context, IAntiforgery antiforgery, SessionRegistry sessions)
    {
        // Exact status codes for the popup's script, not the app's status page.
        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }

        // Liveness first: a page loaded before the session ended holds a token that no longer validates,
        // and the popup should report "signed out" rather than a bad request.
        var user = context.User;
        var sid = SessionClaims.SidOf(user);
        var oid = user.GetObjectId();
        if (!sessions.Status(sid, oid).IsLive || sessions.Find(sid!) is not { } session)
        {
            return Results.Unauthorized();
        }

        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        var flow = context.Request.HasFormContentType ? (await context.Request.ReadFormAsync())["flow"].ToString() : null;
        if (!IsFlow(flow))
        {
            return Results.BadRequest();
        }

        var properties = new AuthenticationProperties { RedirectUri = ConfirmResultUrl(flow, StepUpResults.Confirmed) };
        properties.Items[PurposeKey] = StepUpPurpose;
        properties.Items[SidKey] = session.Sid;
        properties.Items[OidKey] = session.UserId;
        properties.Items[TidKey] = session.TenantId;
        properties.Items[FlowKey] = flow;
        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static void ValidateSignIn(TokenValidatedContext context, AccessOptions access)
    {
        var services = context.HttpContext.RequestServices;
        var principal = context.Principal!;
        var oid = principal.GetObjectId();
        var tid = principal.GetTenantId();
        if (string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(tid))
        {
            context.Fail(new SignInRefusedException());
            return;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var authTime = RecentAuthentication.Parse(principal.FindFirst(SessionClaims.AuthTime)?.Value);
        var logger = Logger(services);

        // prompt=login can be stripped in the browser, so an auth_time that isn't recent shows no credential was entered.
        if (authTime is not null && !RecentAuthentication.IsJustAuthenticated(authTime, now))
        {
            LogSignInAuthTime(logger, "not recent");
            context.Fail(new SignInRefusedException("The ID token's auth_time is not a sign-in just made."));
            return;
        }

        // Without a readable auth_time the session starts but is never fresh.
        LogSignInAuthTime(logger, authTime is null ? "absent" : "present");

        var sid = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var expiresAt = now + access.SessionLifetime;
        Stamp(context, sid, expiresAt);
        context.Properties!.Items[CommitKey] = JsonSerializer.Serialize(
            new SessionRecord(sid, oid, tid, expiresAt, now, authTime, AccessFingerprint(principal)));

        // Whoever the browser was signed in as before, that session ends when this one starts.
        if (PreviousSid(context.HttpContext) is { } previousSid)
        {
            context.Properties.Items[ReplacesKey] = previousSid;
        }
    }

    private static void ValidateStepUp(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var sessions = services.GetRequiredService<SessionRegistry>();
        var properties = context.Properties!;
        var principal = context.Principal!;
        var sid = properties.GetString(SidKey);
        var oid = properties.GetString(OidKey);

        if (!sessions.Status(sid, oid).IsLive || sessions.Find(sid!) is not { } session)
        {
            Refuse(context, StepUpResults.Ended);
            return;
        }

        if (principal.GetObjectId() != oid || principal.GetTenantId() != properties.GetString(TidKey))
        {
            sessions.Remove(session.Sid);
            Refuse(context, StepUpResults.DifferentAccount);
            return;
        }

        // An open circuit keeps the claims it started with, so changed access must not be made freshly authorised.
        if (AccessFingerprint(principal) != session.AccessFingerprint)
        {
            sessions.Remove(session.Sid);
            Refuse(context, StepUpResults.AccessChanged);
            return;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var authTime = RecentAuthentication.Parse(principal.FindFirst(SessionClaims.AuthTime)?.Value);
        if (!RecentAuthentication.IsJustAuthenticated(authTime, now))
        {
            Refuse(context, StepUpResults.NotConfirmed);
            return;
        }

        Stamp(context, session.Sid, session.AbsoluteExpiry);
        properties.Items[CommitKey] = JsonSerializer.Serialize(new StepUpCommit(session.Sid, session.UserId, authTime!.Value));
    }

    private static void Commit(HttpContext context, Dictionary<string, string?> pending, string commit)
    {
        var sessions = context.RequestServices.GetRequiredService<SessionRegistry>();
        if (pending.GetValueOrDefault(PurposeKey) == StepUpPurpose)
        {
            // A no-op unless the session is still live: sign-out or expiry since validation wins.
            var stepUp = JsonSerializer.Deserialize<StepUpCommit>(commit)!;
            sessions.RecordAuthentication(stepUp.Sid, stepUp.Oid, stepUp.AuthenticatedAt);
            return;
        }

        var record = JsonSerializer.Deserialize<SessionRecord>(commit)!;
        if (sessions.IsLive(record))
        {
            sessions.Add(record);
            RememberPreviousSession(context, record);
        }

        if (pending.GetValueOrDefault(ReplacesKey) is { } replaced && replaced != record.Sid)
        {
            sessions.Remove(replaced);
        }
    }

    /// <summary>Ends the browser's remembered session cookie; sign-out leaves no session for a later sign-in to replace.</summary>
    public static void ForgetPreviousSession(HttpContext context) =>
        context.Response.Cookies.Delete(PreviousSessionCookie, PreviousSessionCookieOptions(expires: null));

    // Entra's callback is a cross-site form post, which never carries the Lax sign-in cookie; this one, like
    // the correlation cookie, is SameSite=None so the next sign-in can end the session it names.
    private static void RememberPreviousSession(HttpContext context, SessionRecord record) =>
        context.Response.Cookies.Append(
            PreviousSessionCookie, PreviousSessionProtector(context).Protect(record.Sid), PreviousSessionCookieOptions(record.AbsoluteExpiry));

    private static string? PreviousSid(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(PreviousSessionCookie, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return PreviousSessionProtector(context).Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Tampered or from an old key: there is no previous session to end.
            return null;
        }
    }

    private static IDataProtector PreviousSessionProtector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(PreviousSessionPurpose);

    private static CookieOptions PreviousSessionCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    // The session's id and fixed end go into the cookie, which lasts exactly as long as the session may.
    private static void Stamp(TokenValidatedContext context, string sid, DateTimeOffset expiresAt)
    {
        var identity = context.Principal!.Identities.First();
        foreach (var claim in identity.FindAll(c => c.Type is SessionClaims.Sid or SignInSession.ExpiresClaim).ToList())
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(SessionClaims.Sid, sid));
        identity.AddClaim(SignInSession.ExpiryClaim(expiresAt));
        context.Properties!.ExpiresUtc = expiresAt;
        context.Properties.AllowRefresh = false;
    }

    private static void Refuse(TokenValidatedContext context, string result)
    {
        var logger = Logger(context.HttpContext.RequestServices);
        LogStepUpRefused(logger, result);
        context.Fail(new StepUpRefusedException(context.Properties!.GetString(FlowKey), result));
    }

    private static string AccessFingerprint(ClaimsPrincipal principal) =>
        RecentAuthentication.Fingerprint(
            OpsRoles.Effective(principal.Identities.SelectMany(identity => identity.FindAll(identity.RoleClaimType)).Select(claim => claim.Value)),
            principal.FindAll(GroupsClaim).Select(claim => claim.Value));

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SignInFlow).FullName!);

    [LoggerMessage(EventId = 3501, Level = LogLevel.Information, Message = "Sign-in auth_time {Presence}")]
    private static partial void LogSignInAuthTime(ILogger logger, string presence);

    [LoggerMessage(EventId = 3502, Level = LogLevel.Warning, Message = "Step-up refused: {Failure}")]
    private static partial void LogStepUpRefused(ILogger logger, string failure);

    [GeneratedRegex(@"^[0-9a-f]{32}\z")]
    private static partial Regex FlowPattern();

    private sealed record StepUpCommit(string Sid, string Oid, DateTimeOffset AuthenticatedAt);

    private sealed class SignInRefusedException(string message = "The ID token has no object or tenant id.") : Exception(message);

    private sealed class StepUpRefusedException(string? flow, string result) : Exception($"Step-up refused: {result}")
    {
        public string? Flow { get; } = flow;

        public string Result { get; } = result;
    }
}
