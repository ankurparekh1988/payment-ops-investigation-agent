using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Investigating;

/// <summary>What an investigation logs: outcomes, counts and timings only, never the question, tool output or model text.</summary>
internal static partial class InvestigationLog
{
    public static void Completed(ILogger logger, AssessmentResult result, int gaps, long elapsedMs)
    {
        switch (result)
        {
            case AssessmentResult.Assessed assessed:
                Assessed(logger, assessed.Outcome, assessed.Confidence, assessed.RootCause?.Label, assessed.Claims.Count, gaps, elapsedMs);
                break;
            case AssessmentResult.NotAssessed notAssessed:
                NotAssessed(logger, notAssessed.Reason, gaps, elapsedMs);
                break;
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Investigation assessed: {Outcome}, confidence {Confidence}, root cause {RootCauseLabel}, {Claims} claims, {Gaps} gaps, {ElapsedMs} ms")]
    private static partial void Assessed(ILogger logger, AssessmentOutcome outcome, Confidence confidence, RootCauseLabel? rootCauseLabel, int claims, int gaps, long elapsedMs);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Investigation not assessed: {Reason}, {Gaps} gaps, {ElapsedMs} ms")]
    private static partial void NotAssessed(ILogger logger, NotAssessedReason reason, int gaps, long elapsedMs);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Investigation model call failed: status {Status}, {ElapsedMs} ms")]
    public static partial void ModelFailed(ILogger logger, int? status, long elapsedMs, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "Investigation aborted by an unexpected failure after {ElapsedMs} ms")]
    public static partial void Aborted(ILogger logger, long elapsedMs, Exception exception);
}
