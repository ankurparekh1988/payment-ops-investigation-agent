using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Identity.Web;

using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Security;

internal static class SecurityExtensions
{
    /// <summary>Entra ID sign-in, app-role authorization and the server-side user context.</summary>
    public static IServiceCollection AddPaymentOpsSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(configuration.GetSection("AzureAd"));

        // Sign-in only: the app reaches every service with its managed identity and never calls an
        // API on a user's behalf, so an ID token is all it needs and no client credential exists.
        services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.ResponseType = "id_token";
            options.SignedOutRedirectUri = "/signed-out";
        });

        services.AddAuthorization(AuthorizationPolicies.Configure);
        services.AddCascadingAuthenticationState();

        services.Configure<AccessOptions>(configuration.GetSection(AccessOptions.SectionName));
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<IUserContext, ClaimsUserContext>();
        services.AddScoped<CircuitHandler, UserCircuitHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/sign-out", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return Results.SignOut(
                authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        });

        return endpoints;
    }
}
