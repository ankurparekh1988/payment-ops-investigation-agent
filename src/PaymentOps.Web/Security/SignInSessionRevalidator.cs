using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Identity.Web;

namespace PaymentOps.Web.Security;

/// <summary>
/// Signs an open circuit out when its sign-in ends or its session is no longer live. Without it a circuit
/// keeps the roles and groups it started with for as long as the page stays open.
/// </summary>
internal sealed class SignInSessionRevalidator(ILoggerFactory loggerFactory, TimeProvider time, SessionRegistry sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(15);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var user = authenticationState.User;
        return Task.FromResult(
            SignInSession.IsActive(user, time.GetUtcNow()) && sessions.Status(SessionClaims.SidOf(user), user.GetObjectId()).IsLive);
    }
}
