using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.SmokeTests;

public sealed class HostSmokeTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task Health_endpoint_reports_healthy_without_sign_in()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_reports_healthy_when_no_dependencies_are_configured()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_requires_sign_in()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_requires_an_app_role()
    {
        using var client = factory.CreateClientFor("Noah No-Role", roles: []);

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_shows_the_signed_in_users_access()
    {
        using var client = factory.CreateReaderClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("Riley Reader", html, StringComparison.Ordinal);
        Assert.Contains(OpsRoles.Reader, html, StringComparison.Ordinal);
        Assert.DoesNotContain(OpsRoles.Engineer, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restricted_group_members_are_shown_their_restricted_access()
    {
        using var client = factory.CreateClientFor(
            "Casey Compliance", [OpsRoles.Engineer], PaymentOpsWebApplicationFactory.RestrictedGroupId);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("Yes, through Risk and Compliance", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signed_out_page_is_public()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/signed-out", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
