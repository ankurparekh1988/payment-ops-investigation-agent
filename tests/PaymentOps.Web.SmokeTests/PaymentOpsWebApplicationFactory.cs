using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Domain.Security;
using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests;

/// <summary>The real app, with Entra ID sign-in replaced by <see cref="TestAuthenticationHandler"/>.</summary>
public sealed class PaymentOpsWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string RestrictedGroupId = "8f6c1f0e-3a3b-4c47-9d8e-2b1f5f0c7a11";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:TenantId", TestSessions.TenantId);
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
        builder.UseSetting("PaymentOps:Authorization:RestrictedGroupId", RestrictedGroupId);

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
            });
        });
    }

    /// <summary>A client whose requests are signed in as the given user, with one live session of its own.</summary>
    public HttpClient CreateClientFor(string name, string[] roles, params string[] groups) => SignedIn(this, name, roles, groups);

    /// <summary>The same, for a factory customised with <c>WithWebHostBuilder</c>.</summary>
    public static HttpClient SignedIn(WebApplicationFactory<Program> app, string name, string[] roles, params string[] groups)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The session starts once, here; after that the app's registry alone decides whether it's live.
        var sid = Guid.NewGuid().ToString();
        var oid = Guid.NewGuid().ToString();
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var access = app.Services.GetRequiredService<IOptions<AccessOptions>>().Value;
        app.Services.GetRequiredService<SessionRegistry>().Add(TestSessions.Started(sid, oid, now, access, roles, groups));

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.NameHeader, name);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, string.Join(',', roles));
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.GroupsHeader, string.Join(',', groups));
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SessionHeader, sid);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.OidHeader, oid);
        return client;
    }

    /// <summary>The session id and object id a signed-in client sends.</summary>
    public static (string Sid, string Oid) SessionOf(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return (client.DefaultRequestHeaders.GetValues(TestAuthenticationHandler.SessionHeader).Single(),
            client.DefaultRequestHeaders.GetValues(TestAuthenticationHandler.OidHeader).Single());
    }

    public HttpClient CreateReaderClient() => CreateClientFor("Riley Reader", [OpsRoles.Reader]);
}
