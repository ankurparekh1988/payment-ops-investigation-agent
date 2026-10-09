using System.Globalization;
using System.Security.Claims;

namespace PaymentOps.Web.Security;

/// <summary>When a sign-in ends, recorded as a claim so that open Blazor circuits can see it.</summary>
internal static class SignInSession
{
    public const string ExpiresClaim = "session_expires";

    public static Claim ExpiryClaim(DateTimeOffset expiresAt) =>
        new(ExpiresClaim, expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);

    public static bool IsActive(ClaimsPrincipal user, DateTimeOffset now) =>
        long.TryParse(user.FindFirst(ExpiresClaim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
        && now < DateTimeOffset.FromUnixTimeSeconds(expiresAt);
}
