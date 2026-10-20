using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.Merchants;

namespace PaymentOps.Data.Demo;

internal sealed class DemoMerchantData(DemoDataset dataset) : IMerchantData
{
    public Task<Merchant?> FindAsync(string merchantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(dataset.Merchants.SingleOrDefault(m => m.Id == merchantId));
    }
}
