namespace PaymentOps.Web.Security;

/// <summary>Access settings bound from <c>PaymentOps:Authorization</c>.</summary>
internal sealed class AccessOptions
{
    public const string SectionName = "PaymentOps:Authorization";

    /// <summary>Object ID of the security group whose members can retrieve Restricted knowledge.</summary>
    public string RestrictedGroupId { get; init; } = string.Empty;
}
