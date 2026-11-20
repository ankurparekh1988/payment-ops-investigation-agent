using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

/// <summary>
/// The real app with its real OpenID Connect and cookie handlers. Entra is replaced only at its edges: a fixed
/// configuration and signing key, and a code redemption that hands back an ID token the test signed.
/// </summary>
public sealed class OidcCallbackFactory : WebApplicationFactory<Program>
{
    public const string TenantId = TestSessions.TenantId;
    public const string ClientId = "22222222-2222-2222-2222-222222222222";
    public const string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    public const string AuthorizeEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/authorize";

    private readonly RsaSecurityKey _signingKey = new(RSA.Create(2048)) { KeyId = "test-signing-key" };

    /// <summary>The app's clock, starting at the current whole second so token lifetimes stay valid.</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    /// <summary>Runs between token validation and the cookie sign-in, to change the world in between.</summary>
    public Action? BeforeCookieSignIn { get; set; }

    /// <summary>Runs as the cookie sign-in starts, before the cookie is written.</summary>
    public Action? OnCookieSigningIn { get; set; }

    internal SessionRegistry Registry => Services.GetRequiredService<SessionRegistry>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId", TenantId);
        builder.UseSetting("AzureAd:ClientId", ClientId);
        builder.UseSetting("PaymentOps:Authorization:RestrictedGroupId", PaymentOpsWebApplicationFactory.RestrictedGroupId);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);

            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = Issuer,
                    AuthorizationEndpoint = AuthorizeEndpoint,
                    TokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token",
                    EndSessionEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/logout",
                };
                configuration.SigningKeys.Add(_signingKey);
                var manager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                options.Configuration = configuration;
                options.ConfigurationManager = manager;
                options.TokenValidationParameters.ConfigurationManager = manager;
                options.ProtocolValidator.RequireNonce = false;

                // Microsoft.Identity.Web's AAD issuer validator downloads metadata itself. This keeps its rule,
                // the issuer of the token's own tenant, offline.
                options.TokenValidationParameters.IssuerValidator = (issuer, token, _) =>
                    token is JsonWebToken jwt && jwt.TryGetPayloadValue<string>("tid", out var tid)
                        && issuer == $"https://login.microsoftonline.com/{tid}/v2.0"
                        ? issuer
                        : throw new SecurityTokenInvalidIssuerException("Issuer does not match the token's tenant.");

                // The posted code is the ID token itself, so nothing ever calls a token endpoint.
                options.Events.OnAuthorizationCodeReceived = context =>
                {
                    context.HandleCodeRedemption(null!, context.ProtocolMessage.Code);
                    return Task.CompletedTask;
                };

                var ticketReceived = options.Events.OnTicketReceived;
                options.Events.OnTicketReceived = async context =>
                {
                    BeforeCookieSignIn?.Invoke();
                    await ticketReceived(context);
                };
            });

            services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                var signingIn = options.Events.OnSigningIn;
                options.Events.OnSigningIn = async context =>
                {
                    OnCookieSigningIn?.Invoke();
                    await signingIn(context);
                };
            });
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    /// <summary>An ID token as Entra would issue it, signed with the test key. An empty <paramref name="oid"/> leaves the claim out.</summary>
    public string IdToken(string oid, string tid = TenantId, string[]? roles = null, string[]? groups = null, string? authTime = null, string username = "user@contoso.example")
    {
        var claims = new List<Claim>
        {
            new("tid", tid),
            new("sub", "sub-" + oid),
            new("name", "Test User"),
            new("preferred_username", username),
            new("ver", "2.0"),
        };
        if (oid.Length > 0)
        {
            claims.Add(new Claim("oid", oid));
        }

        claims.AddRange((roles ?? []).Select(role => new Claim("roles", role)));
        claims.AddRange((groups ?? []).Select(group => new Claim("groups", group)));
        if (authTime is not null)
        {
            claims.Add(new Claim("auth_time", authTime, ClaimValueTypes.Integer64));
        }

        // Valid by both the app's clock and the real one, whichever the token handler reads.
        var realNow = DateTime.UtcNow;
        var appNow = Time.GetUtcNow().UtcDateTime;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: tid == TenantId ? Issuer : $"https://login.microsoftonline.com/{tid}/v2.0",
            audience: ClientId,
            subject: new ClaimsIdentity(claims),
            notBefore: (realNow < appNow ? realNow : appNow).AddMinutes(-5),
            expires: (realNow > appNow ? realNow : appNow).AddHours(1),
            issuedAt: (realNow < appNow ? realNow : appNow).AddMinutes(-5),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256));
        return handler.WriteToken(token);
    }

    /// <summary>The <c>auth_time</c> value for a moment relative to the app's clock.</summary>
    public string AuthTime(TimeSpan offset) => (Time.GetUtcNow() + offset).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The state a challenge would have sent to Entra, protected as the handler protects it.</summary>
    public string State(IDictionary<string, string?> items, string correlationId, string redirectUri = "/")
    {
        var options = OidcOptions();
        var properties = new AuthenticationProperties(new Dictionary<string, string?>(items)) { RedirectUri = redirectUri };
        properties.Items[".xsrf"] = correlationId;
        properties.Items[OpenIdConnectDefaults.RedirectUriForCodePropertiesKey] = "http://localhost/signin-oidc";
        properties.Items["code_verifier"] = "test-code-verifier-test-code-verifier-0123456";
        return options.StateDataFormat.Protect(properties);
    }

    /// <summary>Entra's form post back to the app, with the correlation cookie the challenge would have set.</summary>
    public async Task<HttpResponseMessage> CallbackAsync(
        IDictionary<string, string?> items, string idToken, CookieJar? cookies = null, string? redirectUri = null, string? stateOverride = null)
    {
        var (correlationId, cookieHeader) = Correlation(cookies);
        var state = stateOverride ?? State(items, correlationId, redirectUri ?? RedirectUriFor(items));
        return await PostCallback(cookieHeader, [new("code", idToken), new("state", state)]);
    }

    /// <summary>Entra reporting an error instead of a code.</summary>
    public async Task<HttpResponseMessage> ErrorCallbackAsync(IDictionary<string, string?> items, string error, CookieJar? cookies = null)
    {
        var (correlationId, cookieHeader) = Correlation(cookies);
        var state = State(items, correlationId, RedirectUriFor(items));
        return await PostCallback(cookieHeader, [new("error", error), new("error_description", "test"), new("state", state)]);
    }

    public CookieAuthenticationOptions CookieOptions() =>
        Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>Reads the sign-in cookie the way the cookie handler does.</summary>
    public AuthenticationTicket? Ticket(CookieJar cookies) =>
        cookies.Get(".AspNetCore.Cookies") is { } value ? CookieOptions().TicketDataFormat.Unprotect(value) : null;

    private OpenIdConnectOptions OidcOptions() =>
        Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

    private (string CorrelationId, string CookieHeader) Correlation(CookieJar? cookies)
    {
        var correlationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var correlationCookie = OidcOptions().CorrelationCookie.Name + correlationId + "=N";
        // Entra's form post is cross-site: a browser sends only the SameSite=None cookies with it.
        var header = cookies?.CrossSiteHeader() is { Length: > 0 } existing ? existing + "; " + correlationCookie : correlationCookie;
        return (correlationId, header);
    }

    private async Task<HttpResponseMessage> PostCallback(string cookieHeader, KeyValuePair<string, string>[] form)
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/signin-oidc", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Add("Cookie", cookieHeader);
        return await client.SendAsync(request);
    }

    private static string RedirectUriFor(IDictionary<string, string?> items) =>
        items.TryGetValue("paymentops.flow", out var flow) && flow is not null
            ? $"/account/confirm-result?flow={flow}&result=confirmed"
            : "/";
}

/// <summary>A browser's cookies, kept by hand so a test can copy, replay or drop them.</summary>
public sealed class CookieJar
{
    private readonly Dictionary<string, (string Value, bool CrossSite)> _cookies = new(StringComparer.Ordinal);

    public CookieJar Copy()
    {
        var copy = new CookieJar();
        foreach (var (name, cookie) in _cookies)
        {
            copy._cookies[name] = cookie;
        }

        return copy;
    }

    public string? Get(string name) => _cookies.TryGetValue(name, out var cookie) ? cookie.Value : null;

    /// <summary>Sets a cookie as if the server had, for a test that forges one.</summary>
    public void Set(string name, string value, bool crossSite) => _cookies[name] = (value, crossSite);

    public string Header() => string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value.Value));

    /// <summary>The cookies a browser sends on a cross-site request: only those set with SameSite=None.</summary>
    public string CrossSiteHeader() => string.Join("; ", _cookies.Where(c => c.Value.CrossSite).Select(c => c.Key + "=" + c.Value.Value));

    /// <summary>Applies a response's <c>Set-Cookie</c> headers, including deletions.</summary>
    public CookieJar Apply(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        foreach (var (name, value, deleted, crossSite) in SetCookies(response))
        {
            if (deleted)
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = (value, crossSite);
            }
        }

        return this;
    }

    /// <summary>The cookies a response sets, each with whether it deletes the cookie and whether it is sent cross-site.</summary>
    public static IEnumerable<(string Name, string Value, bool Deleted, bool CrossSite)> SetCookies(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            yield break;
        }

        foreach (var header in headers)
        {
            var pair = header.Split(';')[0];
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();
            var deleted = value.Length == 0 || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
            var crossSite = header.Split(';').Any(attribute => attribute.Trim().Equals("samesite=none", StringComparison.OrdinalIgnoreCase));
            yield return (name, value, deleted, crossSite);
        }
    }

    /// <summary>Sends a same-site request carrying these cookies, and keeps what the response sets.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, HttpContent? content = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        if (_cookies.Count > 0)
        {
            request.Headers.Add("Cookie", Header());
        }

        var response = await client.SendAsync(request);
        Apply(response);
        return response;
    }
}
