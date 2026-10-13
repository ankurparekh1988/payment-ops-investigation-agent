using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

internal static class SecurityExtensions
{
    /// <summary>Entra ID sign-in, app-role authorization and the server-side user context.</summary>
    public static IServiceCollection AddPaymentOpsSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var signIn = configuration.GetSection("AzureAd");
        var access = configuration.GetSection(AccessOptions.SectionName);

        if (string.IsNullOrEmpty(signIn["ClientId"]))
        {
            // A new environment runs before its app registration exists: health checks answer, pages don't.
            services.AddAuthentication(SignInNotConfiguredHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, SignInNotConfiguredHandler>(SignInNotConfiguredHandler.SchemeName, null);
        }
        else
        {
            AddEntraSignIn(services, signIn, access.Get<AccessOptions>() ?? new AccessOptions());
        }

        services.AddAuthorization(AuthorizationPolicies.Configure);
        services.AddCascadingAuthenticationState();

        services.Configure<AccessOptions>(access);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuthenticationStateProvider, SignInSessionRevalidator>();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<IUserContext, ClaimsUserContext>();
        services.AddScoped<CircuitHandler, UserCircuitHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous so that a user whose role was removed can still end their session.
        endpoints.MapPost("/account/sign-out", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            return Results.SignOut(
                authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        return endpoints;
    }

    private static void AddEntraSignIn(IServiceCollection services, IConfigurationSection signIn, AccessOptions access)
    {
        // Authorization code flow: the browser only carries a one-time code, which the server redeems
        // with the client credentials in AzureAd:ClientCredentials (the managed identity in Azure).
        // Token acquisition is what redeems the code; the app calls no API as the user.
        services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(signIn)
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SignedOutRedirectUri = "/signed-out";

            // Roles and groups come from the token, so a sign-in lasts a fixed time and changes
            // made in Entra apply at the next one.
            var tokenValidated = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                await tokenValidated(context);
                var expiresAt = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() + access.SessionLifetime;
                context.Properties!.ExpiresUtc = expiresAt;
                context.Properties.AllowRefresh = false;
                context.Principal!.Identities.First().AddClaim(SignInSession.ExpiryClaim(expiresAt));
            };
        });

        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            options.AccessDeniedPath = "/access-denied");
    }
}
