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

        services.AddOptions<AccessOptions>().Bind(access).Validate(AccessOptions.HasValidTimes, AccessOptions.InvalidTimesMessage).ValidateOnStart();
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuthenticationStateProvider, SignInSessionRevalidator>();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<IUserContext, ClaimsUserContext>();
        services.AddScoped<IRecentAuthentication, RecentAuthenticationCheck>();
        services.AddScoped<CircuitHandler, UserCircuitHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous so that a user whose role was removed can still end their session.
        endpoints.MapPost("/account/sign-out", async (HttpContext context, IAntiforgery antiforgery, SessionRegistry sessions) =>
        {
            // The sign-in itself, read afresh: once a session has expired the request's own user is anonymous.
            // In production the default scheme hands this to the cookie.
            var signIn = (await context.AuthenticateAsync()).Principal;
            var sid = signIn is null ? null : SessionClaims.SidOf(signIn);
            var isLive = signIn is not null && sessions.Status(sid, signIn.GetObjectId()).IsLive;

            // Antiforgery tokens are bound to the signed-in user, so a page loaded before expiry can't pass;
            // ending a session that is already dead can't be abused.
            var tokenIsValid = await antiforgery.IsRequestValidAsync(context);
            if (!tokenIsValid && isLive)
            {
                return Results.BadRequest();
            }

            if (sid is not null)
            {
                sessions.Remove(sid);
            }

            if (tokenIsValid)
            {
                return Results.SignOut(
                    authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
            }

            await SessionActivityMiddleware.SignOutOfCookieAsync(context);
            return Results.Redirect("/signed-out");
        }).AllowAnonymous();

        // Anonymous so that an ended session gets a 401 the popup can report, not a sign-in redirect.
        endpoints.MapPost("/account/reauthenticate", SignInFlow.ReauthenticateAsync).AllowAnonymous();

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
            SignInFlow.Configure(options, access);
        });

        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.AccessDeniedPath = "/access-denied";
            SignInFlow.Configure(options);
        });
    }
}
