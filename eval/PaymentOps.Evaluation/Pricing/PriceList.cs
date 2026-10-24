using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Pricing;

/// <summary>Tokens a set of model calls used; cached input is the part of the input the service billed at the cached rate.</summary>
internal sealed record TokenUse(long Input, long CachedInput, long Output)
{
    public static TokenUse Zero { get; } = new(0, 0, 0);

    public TokenUse Add(TokenUse other) => new(Input + other.Input, CachedInput + other.CachedInput, Output + other.Output);
}

/// <param name="EffectiveDate">When the published price took effect.</param>
/// <param name="Retrieved">When it was read from <paramref name="Source"/>.</param>
internal sealed record Price(
    string Model,
    string Version,
    string DeploymentType,
    string Currency,
    decimal InputPerMillion,
    decimal CachedInputPerMillion,
    decimal OutputPerMillion,
    string EffectiveDate,
    string Retrieved,
    string Source);

/// <summary>
/// Dated, sourced prices for the models the reports use. Never fetched during a run; a model with no entry is
/// "not priced", never free. Cost is informational and gates nothing.
/// </summary>
internal sealed class PriceList(IReadOnlyList<Price> prices)
{
    public const int SupportedSchemaVersion = 1;

    public IReadOnlyList<Price> Prices { get; } = prices;

    public static PriceList Load(string path)
    {
        var file = StrictFiles.Read<PriceFile>(path, StrictFiles.CamelCase);
        var problems = file.Prices.SelectMany(Check).ToList();
        if (file.SchemaVersion != SupportedSchemaVersion)
        {
            problems.Add($"schema version {file.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
        }

        return problems.Count == 0 ? new PriceList(file.Prices) : throw new InvalidDataException($"{path}: {string.Join(' ', problems)}");
    }

    public Price? Find(DeploymentIdentity deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        return Prices.FirstOrDefault(p => p.Model == deployment.Model && p.Version == deployment.Version && p.DeploymentType == deployment.DeploymentType);
    }

    /// <summary>The estimated cost, or null when the deployment has no price.</summary>
    public decimal? Cost(DeploymentIdentity deployment, TokenUse use)
    {
        return Find(deployment) is { } price ? Cost(price, use) : null;
    }

    public static decimal Cost(Price price, TokenUse use)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(use);
        return (((use.Input - use.CachedInput) * price.InputPerMillion) + (use.CachedInput * price.CachedInputPerMillion) + (use.Output * price.OutputPerMillion)) / 1_000_000m;
    }

    public static decimal? CostPerValidatedInvestigation(decimal? chatCost, int validated) =>
        chatCost is { } cost && validated > 0 ? cost / validated : null;

    private static IEnumerable<string> Check(Price p)
    {
        var name = $"{p.Model} {p.Version} {p.DeploymentType}";
        if (string.IsNullOrWhiteSpace(p.Currency))
        {
            yield return $"{name}: currency is empty.";
        }

        if (!Uri.TryCreate(p.Source, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps)
        {
            yield return $"{name}: source must be the https address the price was read from.";
        }

        if (!ToolArguments.TryDate(p.EffectiveDate, out _) || !ToolArguments.TryDate(p.Retrieved, out _))
        {
            yield return $"{name}: effectiveDate and retrieved must be dates written YYYY-MM-DD.";
        }

        if (p.InputPerMillion < 0 || p.CachedInputPerMillion < 0 || p.OutputPerMillion < 0)
        {
            yield return $"{name}: prices can't be negative.";
        }
    }

    private sealed record PriceFile(int SchemaVersion, IReadOnlyList<Price> Prices);
}
