using PaymentOps.Domain.Merchants;

namespace PaymentOps.Data.Demo.Faults;

internal sealed class FaultInjectingMerchantData(IMerchantData inner, FaultOptions options) : IMerchantData
{
    public Task<Merchant?> FindAsync(string merchantId, CancellationToken cancellationToken) =>
        FaultInjector.RunAsync(options, DemoDataPorts.Merchants, () => inner.FindAsync(merchantId, cancellationToken), null, cancellationToken);
}
