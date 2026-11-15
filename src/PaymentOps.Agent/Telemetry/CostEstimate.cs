namespace PaymentOps.Agent.Telemetry;

/// <summary>List prices per million tokens, for one deployment.</summary>
public sealed class DeploymentPrice
{
    public string Deployment { get; set; } = string.Empty;

    public decimal InputPerMillion { get; set; }

    public decimal OutputPerMillion { get; set; }
}

/// <summary>
/// Prices for estimating what an investigation cost, from configuration. Without a currency nothing is estimated.
/// List prices: discounts and free allowances aren't modelled, so an estimate is an upper bound.
/// </summary>
public sealed class CostEstimateOptions
{
    public const string SectionName = "PaymentOps:CostEstimate";

    public string? Currency { get; set; }

    /// <summary>A list rather than a map, because App Service setting names can't hold a deployment's hyphen.</summary>
    public List<DeploymentPrice> Deployments { get; set; } = [];

    public decimal? ContentSafetyPerThousandRecords { get; set; }
}

/// <summary>
/// What a turn's usage cost at the configured prices. The amount is given only when the usage is complete and every
/// price it needs is configured; otherwise <see cref="Unavailable"/> says why, and the usage counts still stand.
/// </summary>
public sealed record CostEstimate(decimal? Amount, string? Currency, bool Complete, string? Unavailable)
{
    internal static CostEstimate From(TurnUsage usage, CostEstimateOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Currency))
        {
            return new(null, null, usage.Complete, "no prices");
        }

        if (!usage.Complete)
        {
            return new(null, options.Currency, false, "usage incomplete");
        }

        var amount = 0m;
        foreach (var (deployment, tokens) in usage.Tokens)
        {
            if (options.Deployments.Find(p => string.Equals(p.Deployment, deployment, StringComparison.Ordinal)) is not { } price)
            {
                return new(null, options.Currency, true, $"unpriced deployment {deployment}");
            }

            amount += (tokens.Input * price.InputPerMillion + tokens.Output * price.OutputPerMillion) / 1_000_000m;
        }

        var records = usage.Records.Values.Sum();
        if (records > 0)
        {
            if (options.ContentSafetyPerThousandRecords is not { } perThousand)
            {
                return new(null, options.Currency, true, "no content safety price");
            }

            amount += records * perThousand / 1000m;
        }

        return new(amount, options.Currency, true, null);
    }
}
