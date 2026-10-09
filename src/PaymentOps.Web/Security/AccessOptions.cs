namespace PaymentOps.Web.Security;

/// <summary>Access settings bound from <c>PaymentOps:Authorization</c>.</summary>
internal sealed class AccessOptions
{
    public const string SectionName = "PaymentOps:Authorization";

    /// <summary>Object ID of the security group whose members can retrieve Restricted knowledge.</summary>
    public string RestrictedGroupId { get; init; } = string.Empty;

    /// <summary>How long a sign-in lasts. Role and group changes in Entra apply at the next sign-in.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(8);
}
