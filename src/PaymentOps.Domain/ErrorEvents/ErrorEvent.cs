namespace PaymentOps.Domain.ErrorEvents;

/// <summary>An error our platform recorded while exchanging payments or files with a processor.</summary>
public sealed record ErrorEvent(
    string Id,
    DateTimeOffset OccurredAt,
    string ProcessorId,
    string? MerchantId,
    ErrorCategory Category,
    string Code,
    string? FileId);

public enum ErrorCategory
{
    FileTransferTimeout,
    FileRejected,
    AcknowledgementFailed,
    AuthorizationDeclined,
}
