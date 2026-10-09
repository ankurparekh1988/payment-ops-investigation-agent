using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.SmokeTests;

/// <summary>The real app, with Entra ID sign-in replaced by <see cref="TestAuthenticationHandler"/>.</summary>
public sealed class PaymentOpsWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string RestrictedGroupId = "8f6c1f0e-3a3b-4c47-9d8e-2b1f5f0c7a11";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
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

    /// <summary>A client whose requests are signed in as the given user.</summary>
    public HttpClient CreateClientFor(string name, string[] roles, params string[] groups)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.NameHeader, name);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeader, string.Join(',', roles));
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.GroupsHeader, string.Join(',', groups));
        return client;
    }

    public HttpClient CreateReaderClient() => CreateClientFor("Riley Reader", [OpsRoles.Reader]);
}
