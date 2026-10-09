using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace PaymentOps.Web.Security;

/// <summary>
/// Signs an open circuit out when its sign-in ends. Without it a circuit keeps the roles and groups
/// it started with for as long as the page stays open.
/// </summary>
internal sealed class SignInSessionRevalidator(ILoggerFactory loggerFactory, TimeProvider time)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        Task.FromResult(SignInSession.IsActive(authenticationState.User, time.GetUtcNow()));
}
