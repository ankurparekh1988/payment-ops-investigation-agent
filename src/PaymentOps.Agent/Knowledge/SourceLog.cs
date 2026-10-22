using Microsoft.Extensions.Logging;

namespace PaymentOps.Agent.Knowledge;

// Failure details only: never the section's content.
internal static partial class SourceLog
{
    // Only the exception's type: service messages can echo the request.
    public static void SearchFailed(ILogger logger, string failureClass, int? status, string? errorCode, string? requestId, Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            SearchFailed(logger, failureClass, exception.GetType().Name, status, errorCode, requestId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source not shown: Search failed: {FailureClass} ({ExceptionType}), status {Status}, code {ErrorCode}, request {RequestId}.")]
    private static partial void SearchFailed(ILogger logger, string failureClass, string exceptionType, int? status, string? errorCode, string? requestId);
}
