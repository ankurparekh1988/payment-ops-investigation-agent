using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Running;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Pricing;

/// <summary>
/// What the system's own calls cost across a run's cases: every model call of every attempt, the classifier calls and
/// the Content Safety text records, each at its own published price. Given only when every case's usage is complete and
/// every part is priced in one currency; otherwise <see cref="Status"/> says what stopped it, and the amount is null.
/// </summary>
internal sealed record AllInCost(decimal? Total, decimal? PerValidatedInvestigation, string? Currency, string Status)
{
    public const string Priced = "priced";
    public const string UsageMissing = "usage_missing";
    public const string UsageIncomplete = "usage_incomplete";
    public const string CurrencyMismatch = "currency_mismatch";
    public const string NoValidatedResults = "no_validated_results";

    /// <param name="deployments">The release's deployments by name, which say what each name was priced as.</param>
    public static AllInCost Of(IReadOnlyList<CaseResult> results, IReadOnlyDictionary<string, DeploymentIdentity> deployments, PriceList prices)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(deployments);
        ArgumentNullException.ThrowIfNull(prices);

        if (results.Any(r => r.Usage is null))
        {
            return Inconclusive(UsageMissing);
        }

        if (results.Any(r => r.Usage!.Incomplete.Count > 0))
        {
            return Inconclusive(UsageIncomplete);
        }

        var total = 0m;
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var usage in results.Select(r => r.Usage!))
        {
            foreach (var (name, used) in usage.Deployments.Where(d => d.Value.InputTokens > 0 || d.Value.OutputTokens > 0))
            {
                if (!deployments.TryGetValue(name, out var identity) || prices.Find(identity) is not { } price)
                {
                    return Inconclusive($"unpriced:{name}");
                }

                currencies.Add(price.Currency);
                total += PriceList.Cost(price, new TokenUse(used.InputTokens, used.CachedInputTokens, used.OutputTokens));
            }

            foreach (var (service, records) in usage.ContentSafetyRecords.Where(r => r.Value > 0))
            {
                if (prices.ContentSafetyFor(service) is not { } price)
                {
                    return Inconclusive($"unpriced:{service}");
                }

                currencies.Add(price.Currency);
                total += records * price.PerThousandRecords / 1000m;
            }
        }

        if (currencies.Count > 1)
        {
            return Inconclusive(CurrencyMismatch);
        }

        var validated = results.Count(r => InvestigationCase.AssessedOutcomes.Contains(r.Outcome));
        return validated == 0
            ? Inconclusive(NoValidatedResults)
            : new AllInCost(total, total / validated, currencies.SingleOrDefault(), Priced);
    }

    private static AllInCost Inconclusive(string status) => new(null, null, null, status);
}
