using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Identity.Web;

namespace PaymentOps.Web.Security;

/// <summary>
/// Holds every request to the server's session registry: a sign-in whose session isn't live is signed out
/// before authorization sees it, and only pages and account actions count as activity.
/// </summary>
internal sealed class SessionActivityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SessionRegistry sessions)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var sid = SessionClaims.SidOf(user);
            var userId = user.GetObjectId();
            if (!sessions.Status(sid, userId).IsLive)
            {
                await SignOutOfCookieAsync(context);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
            else if (IsActivity(context))
            {
                sessions.Touch(sid!, userId!);
            }
        }

        await next(context);
    }

    /// <summary>Clears the sign-in cookie, where there is one: hosts without Entra sign-in have no cookie scheme.</summary>
    public static async Task SignOutOfCookieAsync(HttpContext context)
    {
        var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        if (await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme) is not null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    // Circuit traffic, framework scripts, static assets and probes run without the user, so they never keep a session alive.
    private static bool IsActivity(HttpContext context)
    {
        // A re-executed status page answers some other request, which has already been judged.
        if (context.Features.Get<IStatusCodeReExecuteFeature>() is not null)
        {
            return false;
        }

        var path = context.Request.Path;
        if (path.StartsWithSegments("/_blazor") || path.StartsWithSegments("/_framework") || path.StartsWithSegments("/health"))
        {
            return false;
        }

        return path.StartsWithSegments("/account") || context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null;
    }
}
