using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Tools;

namespace PaymentOps.Agent.Telemetry;

/// <summary>
/// The trace, count and decision record of one investigation that ran, whatever its outcome. Refusals before an
/// investigation starts (busy, paused, over a limit) are only counted, by reason: they aren't investigations.
/// </summary>
internal static class InvestigationTelemetry
{
    public const string ErrorOutcome = "error";

    private static readonly Dictionary<NotAssessedReason, string> RejectionReasons = new()
    {
        [NotAssessedReason.Busy] = "busy",
        [NotAssessedReason.Paused] = "paused",
        [NotAssessedReason.DailyLimitReached] = "daily_limit",
        [NotAssessedReason.UsageUnavailable] = "usage_unavailable",
    };

    public static Activity? Start(Guid investigationId, int turn, TraceContext? trace = null)
    {
        var activity = AgentTelemetry.Source.StartActivity("investigation");
        activity?.SetTag("paymentops.investigation.id", investigationId.ToString());
        activity?.SetTag("paymentops.conversation.turn", turn);
        if (trace is not null)
        {
            activity?.SetTag("paymentops.prompt.id", trace.PromptId);
            activity?.SetTag("paymentops.prompt.version", trace.PromptVersion);
            activity?.SetTag("paymentops.chat.deployment", trace.ChatDeployment);
        }

        return activity;
    }

    /// <summary>Low-cardinality: <c>assessed</c>, the not-assessed reason in snake case, or <c>error</c>.</summary>
    public static string Outcome(AssessmentResult? result) => result switch
    {
        AssessmentResult.Assessed => "assessed",
        AssessmentResult.NotAssessed notAssessed => ToolJson.Word(notAssessed.Reason),
        _ => ErrorOutcome,
    };

    public static void Complete(Activity? activity, UsageSnapshot usage, CostEstimateOptions prices, ILogger logger, Guid investigationId, AssessmentResult? result, Exception? failure)
    {
        var outcome = Outcome(result);
        var estimate = CostEstimate.From(usage, prices);
        var (input, output) = (usage.Deployments.Values.Sum(d => d.InputTokens), usage.Deployments.Values.Sum(d => d.OutputTokens));
        var records = usage.ContentSafetyRecords.Values.Sum();
        var requests = usage.HttpRequests.Values.Sum();
        var attempts = Attempts(usage, outcome);

        AgentTelemetry.Investigations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        foreach (var attempt in usage.Attempts)
        {
            AgentTelemetry.Attempts.Add(1, new("tier", ToolJson.Word(attempt.Tier)), new("failure", attempt.Failure is { } f ? ToolJson.Word(f) : "none"));
        }

        if (usage.Escalation is { } escalation and not "none")
        {
            AgentTelemetry.Escalations.Add(1, new KeyValuePair<string, object?>("decision", escalation));
        }
        if (estimate.Amount is { } amount)
        {
            AgentTelemetry.EstimatedCost.Record((double)amount, new KeyValuePair<string, object?>("currency", estimate.Currency));
        }

        if (activity is not null)
        {
            activity.SetTag("paymentops.outcome", outcome);
            activity.SetTag("paymentops.tokens.input", input);
            activity.SetTag("paymentops.tokens.output", output);
            activity.SetTag("paymentops.content_safety.records", records);
            activity.SetTag("paymentops.http.requests", requests);
            activity.SetTag("paymentops.routing", usage.Routing);
            activity.SetTag("paymentops.attempts", attempts);
            activity.SetTag("paymentops.escalation", usage.Escalation);
            activity.SetTag("paymentops.reads.reused", usage.ReusedReads);
            activity.SetTag(estimate.Amount is null ? "paymentops.cost.unavailable" : "paymentops.estimated_cost", estimate.Amount?.ToString(CultureInfo.InvariantCulture) ?? estimate.Unavailable);
            if (failure is not null)
            {
                // The type only: an exception's message can echo what the service was sent.
                activity.SetStatus(ActivityStatusCode.Error, failure.GetType().Name);
            }
        }

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var assessed = result as AssessmentResult.Assessed;
        IEnumerable<Statement> statements = assessed is null ? [] : [assessed.Summary, .. assessed.Claims, .. assessed.RootCause is { } root ? [root.Statement] : Array.Empty<Statement>()];
        var tools = Join(usage.Tools.Select(t => $"{t.Name}:{t.Outcome}"));
        var evidence = Join(statements.SelectMany(s => s.Evidence.Select(e => e.Id)).Distinct(StringComparer.Ordinal));
        var sections = Join(statements.SelectMany(s => s.Citations.Select(c => c.SectionKey)).Distinct(StringComparer.Ordinal));
        var screens = Join(usage.Screens.Select(s => $"{s.Key}={s.Value}"));
        var cacheHits = usage.CacheHits.Values.Sum();
        var deployments = Join(usage.Deployments.Select(d => string.Create(
            CultureInfo.InvariantCulture, $"{d.Key}:{d.Value.LogicalCalls}/{d.Value.HttpAttempts}/{d.Value.InputTokens}/{d.Value.CachedInputTokens}/{d.Value.OutputTokens}")));
        InvestigationLog.Decision(
            logger, investigationId, outcome, tools, evidence, sections, screens, requests, records, input, output, cacheHits, estimate.Amount, estimate.Currency, estimate.Unavailable,
            usage.Routing ?? "none", attempts, usage.Escalation ?? "none", usage.ReusedReads, deployments, usage.EstimateExceeded);
    }

    /// <summary>A request refused before any investigation ran: counted by reason, nothing else recorded.</summary>
    public static void Rejected(NotAssessedReason reason) =>
        AgentTelemetry.Rejected.Add(1, new KeyValuePair<string, object?>("reason", RejectionReasons.TryGetValue(reason, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(reason), reason, "Only a refusal before an investigation starts is a rejection.")));

    private static string Join(IEnumerable<string> values) => string.Join(',', values);

    // Each attempt's tier and how it ended, in order: a failure, or the investigation's outcome for the one that answered.
    private static string Attempts(UsageSnapshot usage, string outcome) =>
        string.Join('>', usage.Attempts.Select(a => $"{ToolJson.Word(a.Tier)}:{(a.Failure is { } f ? ToolJson.Word(f) : outcome)}"));
}
