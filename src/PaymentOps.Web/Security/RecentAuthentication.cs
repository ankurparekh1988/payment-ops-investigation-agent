using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PaymentOps.Web.Security;

/// <summary>Whether, and from what, a user authenticated recently enough for a sensitive action.</summary>
internal static class RecentAuthentication
{
    /// <summary>Clock difference tolerated between Entra and this server.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    /// <summary>How old an <c>auth_time</c> may be for a step-up sign-in to count as just done.</summary>
    public static readonly TimeSpan StepUpMaxAge = TimeSpan.FromMinutes(5);

    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>Reads an <c>auth_time</c> claim. Anything that is not plain Unix seconds in range is null.</summary>
    public static DateTimeOffset? Parse(string? authTimeClaim) =>
        long.TryParse(authTimeClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    public static bool IsFresh(DateTimeOffset? authenticatedAt, DateTimeOffset now, TimeSpan window) =>
        authenticatedAt is { } at && at > now - window && at <= now + Skew;

    public static bool IsJustAuthenticated(DateTimeOffset? authTime, DateTimeOffset now) =>
        authTime is { } at && at >= now - StepUpMaxAge && at <= now + Skew;

    /// <summary>Identifies a user's roles and groups, so a change in either can be noticed without storing them.</summary>
    public static string Fingerprint(IEnumerable<string> roles, IEnumerable<string> groupIds)
    {
        var sortedRoles = roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var sortedGroups = groupIds.Select(g => g.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        // A separator that cannot occur in a value keeps ["a","b"] + ["c"] apart from ["a"] + ["b","c"].
        var text = string.Join('\u001f', sortedRoles) + "\n" + string.Join('\u001f', sortedGroups);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
