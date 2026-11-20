using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests;

/// <summary>
/// Signs a request in from test headers, with the claim types Entra ID issues, so tests exercise the
/// app's real authorization without a live sign-in. A request without session headers stays anonymous.
/// Whether the session is live is left to the app's own registry, as in production.
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string NameHeader = "X-Test-Name";
    public const string RolesHeader = "X-Test-Roles";
    public const string GroupsHeader = "X-Test-Groups";
    public const string SessionHeader = "X-Test-Session";
    public const string OidHeader = "X-Test-Oid";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasSession = Request.Headers.TryGetValue(SessionHeader, out var sid);
        var hasOid = Request.Headers.TryGetValue(OidHeader, out var oid);
        if (!hasSession && !hasOid)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();
        if (Request.Headers.TryGetValue(NameHeader, out var name))
        {
            claims.Add(new Claim("name", name.ToString()));
        }

        if (hasOid)
        {
            claims.Add(new Claim("oid", oid.ToString()));
        }

        if (hasSession)
        {
            claims.Add(new Claim(SessionClaims.Sid, sid.ToString()));
        }

        claims.AddRange(Values(RolesHeader).Select(role => new Claim("roles", role)));
        claims.AddRange(Values(GroupsHeader).Select(group => new Claim("groups", group)));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(TestPrincipal.From(claims), SchemeName)));
    }

    private string[] Values(string header) =>
        Request.Headers.TryGetValue(header, out var value)
            ? value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}

/// <summary>A signed-in principal shaped like one built from an Entra ID token.</summary>
internal static class TestPrincipal
{
    public static ClaimsPrincipal From(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, TestAuthenticationHandler.SchemeName, nameType: "name", roleType: "roles"));
}

/// <summary>Live sessions for tests that build the user context directly.</summary>
internal static class TestSessions
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";

    /// <summary>A record for a session that starts now and lasts the configured lifetime.</summary>
    public static SessionRecord Started(string sid, string userId, DateTimeOffset now, AccessOptions access, IEnumerable<string>? roles = null, IEnumerable<string>? groups = null) =>
        new(sid, userId, TenantId, now + access.SessionLifetime, now, now, RecentAuthentication.Fingerprint(roles ?? [], groups ?? []));

    /// <summary>
    /// A registry holding a live session for the principal, which gains the session's claim. A principal
    /// without an <c>oid</c> claim, such as an anonymous one, gets no session.
    /// </summary>
    public static SessionRegistry LiveFor(ClaimsPrincipal principal, AccessOptions access)
    {
        var registry = new SessionRegistry(TimeProvider.System, Options.Create(access));
        if (principal.FindFirst("oid")?.Value is { } oid && principal.Identities.FirstOrDefault() is { } identity)
        {
            var sid = Guid.NewGuid().ToString();
            identity.AddClaim(new Claim(SessionClaims.Sid, sid));
            registry.Add(Started(sid, oid, TimeProvider.System.GetUtcNow(), access));
        }

        return registry;
    }
}
