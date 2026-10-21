using PaymentOps.Data.Demo.Dataset;
using PaymentOps.Domain.Merchants;

namespace PaymentOps.Data.Demo;

internal sealed class DemoMerchantData(DemoDataset dataset) : IMerchantData
{
    // Ids are unique: the loader checked them.
    public Task<Merchant?> FindAsync(string merchantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(dataset.Merchants.FirstOrDefault(m => m.Id == merchantId));
    }
}
