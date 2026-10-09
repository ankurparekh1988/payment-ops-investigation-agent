using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests;

public sealed class SignInTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task Without_an_app_registration_health_checks_answer_and_pages_report_sign_in_unavailable()
    {
        using var unconfigured = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("AzureAd:ClientId", string.Empty));
        using var client = unconfigured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative));
        using var home = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, home.StatusCode);
    }

    [Fact]
    public void Sign_in_uses_the_authorization_code_flow_with_pkce()
    {
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal(OpenIdConnectResponseType.Code, oidc.ResponseType);
        Assert.True(oidc.UsePkce);
    }

    [Fact]
    public void Users_without_a_role_are_sent_to_a_page_they_can_reach()
    {
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("/access-denied", cookie.AccessDeniedPath);
    }

    [Fact]
    public async Task Access_denied_page_is_public()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/access-denied", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sign_out_needs_no_role_and_rejects_a_missing_antiforgery_token()
    {
        using var client = factory.CreateClientFor("Noah No-Role", roles: []);

        using var response = await client.PostAsync(new Uri("/account/sign-out", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void A_sign_in_ends_at_its_recorded_expiry()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var user = TestPrincipal.From([SignInSession.ExpiryClaim(expiresAt)]);

        Assert.True(SignInSession.IsActive(user, expiresAt.AddMinutes(-1)));
        Assert.False(SignInSession.IsActive(user, expiresAt.AddMinutes(1)));
        Assert.False(SignInSession.IsActive(new ClaimsPrincipal(), DateTimeOffset.UtcNow));
    }
}
