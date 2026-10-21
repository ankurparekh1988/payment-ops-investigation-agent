namespace PaymentOps.Domain.Settlement;

/// <summary>
/// A captured payment. <paramref name="ProcessorCapturedAt"/> is the processor's own capture time, which
/// decides its batch; <paramref name="BatchDate"/> is null until a settlement file includes the payment.
/// </summary>
public sealed record Payment(
    string Id,
    string MerchantId,
    string ProcessorId,
    decimal Amount,
    string Currency,
    DateTimeOffset CapturedAt,
    DateTimeOffset? ProcessorCapturedAt,
    DateOnly? BatchDate);
