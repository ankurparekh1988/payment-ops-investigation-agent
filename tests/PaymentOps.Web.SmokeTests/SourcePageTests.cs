using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Security;

namespace PaymentOps.Web.SmokeTests;

public sealed class SourcePageTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    private const string Key = "doc-a--aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task A_found_section_is_shown()
    {
        using var client = ClientWith(_ => new SourceResult.Found("Doc A", "Doc A > One", "Some **text**."));

        using var response = await client.GetAsync(new Uri($"/sources/{Key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Doc A &gt; One", html, StringComparison.Ordinal);
        Assert.Contains("<strong>text</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_source_page_is_a_whole_document_so_relative_links_and_styles_resolve()
    {
        using var client = ClientWith(_ => new SourceResult.Found("Doc A", "Doc A > One", "Text."));

        using var response = await client.GetAsync(new Uri($"/sources/{Key}", UriKind.Relative));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"/\"", html, StringComparison.Ordinal);
        Assert.Contains("<title>Source</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_and_unauthorized_sections_get_the_apps_ordinary_404()
    {
        using var client = ClientWith(_ => new SourceResult.NotFound());

        using var response = await client.GetAsync(new Uri($"/sources/{Key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("the content you are looking for does not exist", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Restricted", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_being_down_is_503()
    {
        using var client = ClientWith(_ => new SourceResult.Unavailable());

        using var response = await client.GetAsync(new Uri($"/sources/{Key}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("isn't responding right now", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_requests_never_reach_the_source()
    {
        var called = false;
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IInvestigationService>(_ =>
            new FakeInvestigationService(new InvestigationResult.NotAnswered(NotAnsweredReason.NoEvidence), _ =>
            {
                called = true;
                return new SourceResult.NotFound();
            }))));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri($"/sources/{Key}", UriKind.Relative));

        Assert.False(called);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient ClientWith(Func<string, SourceResult> source) =>
        PaymentOpsWebApplicationFactory.SignedIn(
            factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IInvestigationService>(_ =>
                new FakeInvestigationService(new InvestigationResult.NotAnswered(NotAnsweredReason.NoEvidence), source)))),
            "Riley Reader",
            [OpsRoles.Reader]);
}
