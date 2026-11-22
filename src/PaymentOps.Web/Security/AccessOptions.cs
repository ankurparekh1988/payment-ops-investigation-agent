namespace PaymentOps.Web.Security;

/// <summary>Access settings bound from <c>PaymentOps:Authorization</c>.</summary>
internal sealed class AccessOptions
{
    public const string SectionName = "PaymentOps:Authorization";

    public const string InvalidTimesMessage =
        "PaymentOps:Authorization needs idle and recent-authentication times of 1 to 60 minutes, a warning shorter than the idle time, and a lifetime longer than it.";

    /// <summary>Object ID of the security group whose members can retrieve Restricted knowledge.</summary>
    public string RestrictedGroupId { get; init; } = string.Empty;

    /// <summary>How long a sign-in lasts. Role and group changes in Entra apply at the next sign-in.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);

    /// <summary>How long a session may sit without activity before the server ends it.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>How long before <see cref="IdleTimeout"/> runs out the user is warned.</summary>
    public TimeSpan IdleWarning { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How recently the user must have authenticated to perform a sensitive action.</summary>
    public TimeSpan RecentAuthenticationWindow { get; init; } = TimeSpan.FromMinutes(15);

    public static bool HasValidTimes(AccessOptions options) =>
        InOneToSixtyMinutes(options.IdleTimeout)
        && InOneToSixtyMinutes(options.RecentAuthenticationWindow)
        && options.IdleWarning > TimeSpan.Zero
        && options.IdleWarning < options.IdleTimeout
        && options.SessionLifetime > options.IdleTimeout;

    private static bool InOneToSixtyMinutes(TimeSpan value) => value >= TimeSpan.FromMinutes(1) && value <= TimeSpan.FromMinutes(60);
}
