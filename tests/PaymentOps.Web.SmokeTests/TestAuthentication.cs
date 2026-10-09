using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentOps.Web.SmokeTests;

/// <summary>
/// Signs a request in from test headers, with the claim types Entra ID issues, so tests exercise the
/// app's real authorization without a live sign-in. A request without headers stays anonymous.
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string NameHeader = "X-Test-Name";
    public const string RolesHeader = "X-Test-Roles";
    public const string GroupsHeader = "X-Test-Groups";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(NameHeader, out var name))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("name", name.ToString()), new("oid", Guid.NewGuid().ToString()) };
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
