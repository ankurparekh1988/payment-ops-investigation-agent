namespace PaymentOps.Web.Health;

/// <summary>Azure AI Search settings, bound from <c>PaymentOps:Search</c>.</summary>
internal sealed class SearchOptions
{
    public const string SectionName = "PaymentOps:Search";

    /// <summary>Search endpoint. Unset locally and in tests, where questions can't be answered.</summary>
    public Uri? Endpoint { get; init; }
}
