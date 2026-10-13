using System.Security.Claims;

namespace PaymentOps.Web.Security;

/// <summary>
/// Holds the signed-in user for the current scope: an HTTP request, or a Blazor circuit, where
/// HttpContext isn't available. Set by <see cref="CurrentUserMiddleware"/> and <see cref="UserCircuitHandler"/>.
/// </summary>
internal sealed class CurrentUserAccessor
{
    public ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());
}
