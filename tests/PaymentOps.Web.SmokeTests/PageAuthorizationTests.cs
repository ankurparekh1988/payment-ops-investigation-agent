using System.Reflection;

using Bunit.TestDoubles;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Web.Components.Layout;

namespace PaymentOps.Web.SmokeTests;

/// <summary>
/// Inside a circuit, navigation isn't an HTTP request, so the fallback role policy doesn't run: every interactive page
/// states its own requirement, and a circuit whose sign-in ended is sent through a full load, where the HTTP checks apply.
/// </summary>
public sealed class PageAuthorizationTests : InteractiveTestContext
{
    public static TheoryData<string> InteractivePages()
    {
        var data = new TheoryData<string>();
        foreach (var page in typeof(Program).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any())
            .Where(t => t.GetCustomAttribute<ExcludeFromInteractiveRoutingAttribute>() is null))
        {
            data.Add(page.FullName!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InteractivePages))]
    public void Every_interactive_page_requires_a_role(string pageName)
    {
        var page = typeof(Program).Assembly.GetType(pageName)!;

        var authorize = page.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Investigate", authorize.Policy);
        Assert.Null(page.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void The_home_and_investigate_pages_are_interactive() =>
        Assert.Equal(2, InteractivePages().Count);

    [Fact]
    public void A_circuit_without_access_is_sent_through_a_full_load()
    {
        var navigation = Context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("investigate");

        Render<SignInAgain>();

        var last = ((BunitNavigationManager)navigation).History.First();
        Assert.True(last.Options.ForceLoad);
        Assert.EndsWith("/investigate", last.Uri, StringComparison.Ordinal);
    }
}
