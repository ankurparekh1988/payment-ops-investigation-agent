using System.Net;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.SmokeTests;

/// <summary>
/// The app's own pages outside the signed-in experience: plain, in the company shell, explaining nothing about the
/// product. Someone without a role sees who they're signed in as and can sign out, but no navigation.
/// </summary>
public sealed class StatusPagesTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task Signed_out_offers_sign_in_in_the_shell()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/signed-out", UriKind.Relative));

        Assert.Contains("Calderbrook Payments", html, StringComparison.Ordinal);
        Assert.Contains("You&#x27;re signed out</h1>", html, StringComparison.Ordinal);
        Assert.Matches("<a[^>]*href=\"/\"[^>]*>Sign in</a>", html);
        Assert.DoesNotContain("href=\"investigate\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_access_shows_the_signed_in_account_and_sign_out()
    {
        using var client = factory.CreateClientFor("Noah No-Role", roles: []);

        var html = await client.GetStringAsync(new Uri("/access-denied", UriKind.Relative));

        Assert.Contains("You don&#x27;t have access to Payment Operations</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Ask your administrator to assign you a role.", html, StringComparison.Ordinal);
        Assert.Contains("Signed in as Noah No-Role", html, StringComparison.Ordinal);
        Assert.Contains("action=\"account/sign-out\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"investigate\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_found_is_a_404_in_the_shell()
    {
        using var client = factory.CreateReaderClient();

        using var response = await client.GetAsync(new Uri("/no-such-page", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Calderbrook Payments", html, StringComparison.Ordinal);
        Assert.Matches("<a[^>]*href=\"/\"[^>]*>Go to Home</a>", html);
    }

    [Fact]
    public async Task The_error_page_is_plain_and_gives_a_reference()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/Error", UriKind.Relative));

        Assert.Contains("Something went wrong</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Reference", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Development", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_in_reader_keeps_the_navigation_on_status_pages()
    {
        using var client = factory.CreateClientFor("Riley Reader", [OpsRoles.Reader]);

        using var response = await client.GetAsync(new Uri("/no-such-page", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"investigate\"", html, StringComparison.Ordinal);
    }
}
