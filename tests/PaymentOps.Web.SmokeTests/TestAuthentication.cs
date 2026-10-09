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

        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "name", roleType: "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private string[] Values(string header) =>
        Request.Headers.TryGetValue(header, out var value)
            ? value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
