using PaymentOps.Evaluation.Pricing;
using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Evaluation.Tests;

public sealed class PriceListTests
{
    private static readonly string RealPath = Path.Combine(RepositoryRoot.FullPath, "eval", "pricing.json");
    private static readonly DeploymentIdentity Mini = new("gpt-5.4-mini", "2026-03-17", "GlobalStandard", "strict");

    private static string File(string price)
    {
        var path = Path.GetTempFileName();
        System.IO.File.WriteAllText(path, $$"""{ "schemaVersion": 1, "prices": [ {{price}} ] }""");
        return path;
    }

    private const string Valid = """
        { "model": "gpt-5.4-mini", "version": "2026-03-17", "deploymentType": "GlobalStandard", "currency": "USD",
          "inputPerMillion": 0.75, "cachedInputPerMillion": 0.075, "outputPerMillion": 4.5,
          "effectiveDate": "2026-03-01", "retrieved": "2026-10-23", "source": "https://prices.azure.com/api/retail/prices" }
        """;

    [Fact]
    public void Every_price_states_currency_effective_date_and_source()
    {
        var prices = PriceList.Load(RealPath);

        Assert.All(prices.Prices, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Currency));
            Assert.StartsWith("https://", p.Source, StringComparison.Ordinal);
            Assert.True(DateOnly.TryParseExact(p.EffectiveDate, "yyyy-MM-dd", out _));
        });
        Assert.NotNull(prices.Find(Mini));
    }

    [Fact]
    public void A_missing_currency_or_source_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => PriceList.Load(File(Valid.Replace("\"USD\"", "\"\"", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => PriceList.Load(File(Valid.Replace("https://prices.azure.com/api/retail/prices", "the pricing page", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => PriceList.Load(File(Valid.Replace("\"retrieved\"", "\"fetched\"", StringComparison.Ordinal))));
    }

    [Fact]
    public void Cost_prices_input_cached_input_and_output_separately()
    {
        var prices = PriceList.Load(File(Valid));

        // 1M input of which 200k cached, and 100k output: 0.8 x 0.75 + 0.2 x 0.075 + 0.1 x 4.5.
        Assert.Equal(1.065m, prices.Cost(Mini, new TokenUse(1_000_000, 200_000, 100_000)));
    }

    [Fact]
    public void An_unpriced_deployment_costs_null_not_zero() =>
        Assert.Null(PriceList.Load(File(Valid)).Cost(Mini with { Version = "2027-01-01" }, new TokenUse(1000, 0, 100)));

    [Fact]
    public void Cost_per_validated_investigation_divides_by_validated_results()
    {
        Assert.Equal(0.25m, PriceList.CostPerValidatedInvestigation(1m, 4));
        Assert.Null(PriceList.CostPerValidatedInvestigation(1m, 0));
        Assert.Null(PriceList.CostPerValidatedInvestigation(null, 4));
    }
}
