using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Model;

namespace PaymentOps.Agent.Investigating;

/// <summary>What an investigation logs: outcomes, counts and timings only, never the question, tool output or model text.</summary>
internal static partial class InvestigationLog
{
    public static void Completed(ILogger logger, AssessmentResult result, int gaps, long elapsedMs)
    {
        switch (result)
        {
            case AssessmentResult.Assessed assessed:
                Assessed(logger, result.InvestigationId, assessed.Outcome, assessed.Confidence, assessed.RootCause?.Label, assessed.Claims.Count, gaps, elapsedMs);
                break;
            case AssessmentResult.NotAssessed notAssessed:
                NotAssessed(logger, result.InvestigationId, notAssessed.Reason, gaps, elapsedMs);
                break;
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Investigation {InvestigationId} assessed: {Outcome}, confidence {Confidence}, root cause {RootCauseLabel}, {Claims} claims, {Gaps} gaps, {ElapsedMs} ms")]
    private static partial void Assessed(ILogger logger, Guid investigationId, AssessmentOutcome outcome, Confidence confidence, RootCauseLabel? rootCauseLabel, int claims, int gaps, long elapsedMs);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Investigation {InvestigationId} not assessed: {Reason}, {Gaps} gaps, {ElapsedMs} ms")]
    private static partial void NotAssessed(ILogger logger, Guid investigationId, NotAssessedReason reason, int gaps, long elapsedMs);

    // Only the exception's type and the service's codes: service messages can echo the request, and so the question.
    public static void ModelFailed(ILogger logger, Guid investigationId, Exception exception, long elapsedMs)
    {
        var attempt = ModelFailure.LastAttempt(exception);
        ModelFailed(logger, investigationId, exception.GetType().Name, attempt?.Status, attempt is null ? null : ModelFailure.ErrorCode(attempt), attempt is null ? null : ModelFailure.RequestId(attempt), elapsedMs);
    }

    /// <param name="side"><c>prompt</c> when the request was refused, <c>completion</c> when the answer was withheld.</param>
    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Investigation {InvestigationId} blocked by the content filter on the {Side}, {ElapsedMs} ms")]
    public static partial void Blocked(ILogger logger, Guid investigationId, string side, long elapsedMs);

    public static void Aborted(ILogger logger, Guid investigationId, Exception exception, long elapsedMs) => Aborted(logger, investigationId, exception.GetType().Name, elapsedMs);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Investigation {InvestigationId} model call failed: {ExceptionType}, status {Status}, code {ErrorCode}, request {RequestId}, {ElapsedMs} ms")]
    private static partial void ModelFailed(ILogger logger, Guid investigationId, string exceptionType, int? status, string? errorCode, string? requestId, long elapsedMs);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Investigation {InvestigationId} {Tool}: {Screened} untrusted values screened, {Flagged} flagged and {Unscreenable} unscreenable, withheld from the model")]
    public static partial void TextWithheld(ILogger logger, Guid investigationId, string tool, int screened, int flagged, int unscreenable);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "Investigation {InvestigationId} aborted by an unexpected failure: {ExceptionType}, after {ElapsedMs} ms")]
    private static partial void Aborted(ILogger logger, Guid investigationId, string exceptionType, long elapsedMs);
}
