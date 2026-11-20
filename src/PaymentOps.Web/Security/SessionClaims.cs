using System.Security.Claims;

namespace PaymentOps.Web.Security;

/// <summary>Claim names that tie a sign-in cookie to its server-side session.</summary>
internal static class SessionClaims
{
    public const string Sid = "paymentops_sid";

    /// <summary>The OpenID Connect claim for when the user last authenticated, in Unix seconds.</summary>
    public const string AuthTime = "auth_time";

    public static string? SidOf(ClaimsPrincipal user) => user.FindFirst(Sid)?.Value;
}
