namespace PaymentOps.Domain.Merchants;

/// <summary>The merchant system of record. Ids are matched exactly.</summary>
public interface IMerchantData
{
    /// <summary>The merchant, or null if there's no merchant with that id.</summary>
    Task<Merchant?> FindAsync(string merchantId, CancellationToken cancellationToken);
}
