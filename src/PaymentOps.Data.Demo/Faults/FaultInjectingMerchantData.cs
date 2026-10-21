using PaymentOps.Domain.Merchants;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingMerchantData(IMerchantData inner, FaultInjector faults) : IMerchantData
{
    public Task<Merchant?> FindAsync(string merchantId, CancellationToken cancellationToken) =>
        faults.RunAsync(() => inner.FindAsync(merchantId, cancellationToken), null, cancellationToken);
}
