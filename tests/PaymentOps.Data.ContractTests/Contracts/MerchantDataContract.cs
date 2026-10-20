using PaymentOps.Domain.Merchants;

namespace PaymentOps.Data.ContractTests.Contracts;

/// <summary>What every <see cref="IMerchantData"/> must do. An implementation's tests inherit this class.</summary>
public abstract class MerchantDataContract
{
    protected abstract IMerchantData Create(ContractData data);

    private IMerchantData Port => Create(new ContractData());

    [Fact]
    public async Task A_merchant_is_found_by_id_with_its_routing_and_hold()
    {
        var merchant = await Port.FindAsync("M-3", CancellationToken.None);

        Assert.NotNull(merchant);
        Assert.Equal(["p-b"], merchant.ProcessorIds);
        Assert.Equal("RF-37", merchant.RiskHold!.RuleId);
    }

    [Theory]
    [InlineData("M-404")]
    [InlineData("m-1")]
    [InlineData(" M-1")]
    public async Task An_unknown_id_finds_nothing_and_ids_match_exactly(string id) =>
        Assert.Null(await Port.FindAsync(id, CancellationToken.None));

    [Fact]
    public async Task A_cancelled_token_throws() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port.FindAsync("M-1", ContractData.Cancelled()));
}
