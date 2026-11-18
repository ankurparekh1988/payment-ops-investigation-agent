namespace PaymentOps.Agent.Telemetry;

/// <summary>List prices per million tokens, for one deployment.</summary>
public sealed class DeploymentPrice
{
    public string Deployment { get; set; } = string.Empty;

    public decimal InputPerMillion { get; set; }

    /// <summary>Input served from the prompt cache; without it, cached input is priced as input, an upper bound.</summary>
    public decimal? CachedInputPerMillion { get; set; }

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
    internal static CostEstimate From(TurnUsage usage, CostEstimateOptions options) => From(usage.Snapshot(), options);

    internal static CostEstimate From(UsageSnapshot usage, CostEstimateOptions options)
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
        foreach (var (deployment, used) in usage.Deployments)
        {
            if (used.InputTokens == 0 && used.OutputTokens == 0)
            {
                continue;
            }

            if (options.Deployments.Find(p => string.Equals(p.Deployment, deployment, StringComparison.Ordinal)) is not { } price)
            {
                return new(null, options.Currency, true, $"unpriced deployment {deployment}");
            }

            // Reported input includes the cached part, which is billed at its own rate.
            var cached = Math.Min(used.CachedInputTokens, used.InputTokens);
            amount += ((used.InputTokens - cached) * price.InputPerMillion
                + cached * (price.CachedInputPerMillion ?? price.InputPerMillion)
                + used.OutputTokens * price.OutputPerMillion) / 1_000_000m;
        }

        var records = usage.ContentSafetyRecords.Values.Sum();
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
