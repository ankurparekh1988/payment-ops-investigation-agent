using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Answering;

// Counts, keys, timings and failure details only: never the question, evidence, prompt or model output.
internal static partial class GroundedAnswerLog
{
    public static void Completed(ILogger logger, InvestigationResult result, RetrievalMode mode, IReadOnlyList<RetrievedSection> sections, long retrievalMs, long? modelMs, UsageDetails? usage)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var outcome = result is InvestigationResult.NotAnswered notAnswered ? notAnswered.Reason.ToString() : "Answered";
        var keys = string.Join(',', sections.Select(s => s.Key));
        Completed(logger, outcome, mode, sections.Count, keys, retrievalMs, modelMs, usage?.InputTokenCount, usage?.OutputTokenCount);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Grounded answer: {Outcome}. Mode {Mode}; {HitCount} hits; sections {SectionKeys}; retrieval {RetrievalMs} ms; model {ModelMs} ms; tokens in {InputTokens}, out {OutputTokens}.")]
    private static partial void Completed(ILogger logger, string outcome, RetrievalMode mode, int hitCount, string sectionKeys, long retrievalMs, long? modelMs, long? inputTokens, long? outputTokens);

    // Only the exception's type: service messages can echo the request body, and so the question.
    public static void DependencyFailed(ILogger logger, NotAnsweredReason reason, string dependency, string failureClass, int? status, string? errorCode, string? requestId, Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            var exceptionType = exception.GetType().Name;
            DependencyFailed(logger, reason, dependency, failureClass, status, errorCode, requestId, exceptionType);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Grounded answer not given: {Reason}. Dependency {Dependency} failed: {FailureClass} ({ExceptionType}), status {Status}, code {ErrorCode}, request {RequestId}.")]
    private static partial void DependencyFailed(ILogger logger, NotAnsweredReason reason, string dependency, string failureClass, int? status, string? errorCode, string? requestId, string exceptionType);
}
