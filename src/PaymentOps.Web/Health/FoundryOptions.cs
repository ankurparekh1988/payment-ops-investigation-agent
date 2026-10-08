namespace PaymentOps.Web.Health;

/// <summary>Connection settings for Microsoft Foundry, bound from <c>PaymentOps:Foundry</c>.</summary>
internal sealed class FoundryOptions
{
    public const string SectionName = "PaymentOps:Foundry";

    /// <summary>Foundry resource endpoint. Unset locally and in tests, which disables the readiness check.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Entra token scope for Azure AI services. Differs in sovereign clouds.</summary>
    public string TokenScope { get; init; } = string.Empty;
}
