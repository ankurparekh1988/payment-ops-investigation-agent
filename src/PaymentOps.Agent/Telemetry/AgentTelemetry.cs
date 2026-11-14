using System.Diagnostics;
using System.Diagnostics.Metrics;

using PaymentOps.Agent.Contracts;

namespace PaymentOps.Agent.Telemetry;

/// <summary>
/// The agent's traces and metrics, under one name the host subscribes to. Tags carry outcomes, services, deployments
/// and system identifiers only: never a question, an answer, tool output, model text or who asked.
/// </summary>
public static class AgentTelemetry
{
    public const string Name = "PaymentOps.Agent";

    internal static readonly ActivitySource Source = new(Name);
    internal static readonly Meter Meter = new(Name);

    internal static readonly Counter<long> Investigations = Meter.CreateCounter<long>("paymentops.investigations", description: "Investigations that ran, by outcome.");
    internal static readonly Counter<long> Rejected = Meter.CreateCounter<long>("paymentops.investigations.rejected", description: "Requests refused before an investigation started, by reason only.");
    internal static readonly Counter<long> ScreenBlocks = Meter.CreateCounter<long>("paymentops.screen.blocks", description: "Questions blocked and tool text withheld, by kind.");
    internal static readonly Counter<long> ModelTokens = Meter.CreateCounter<long>("paymentops.model.tokens", "{token}", "Model tokens, by direction and deployment.");
    internal static readonly Counter<long> HttpRequests = Meter.CreateCounter<long>("paymentops.http.requests", "{request}", "Requests sent to paid services, retries included.");
    internal static readonly Counter<long> ContentSafetyRecords = Meter.CreateCounter<long>("paymentops.content_safety.records", "{record}", "Content Safety text records answered, as billed.");
    internal static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("paymentops.screen.cache_hits", "{hit}", "Screening verdicts served from a cache, sending nothing.");
    internal static readonly Histogram<double> EstimatedCost = Meter.CreateHistogram<double>("paymentops.investigation.estimated_cost", description: "Estimated cost of an investigation whose usage and prices were complete.");

    /// <summary>Counts a request refused before any investigation ran (busy, paused, over a limit, usage unknown); nothing else about it is recorded.</summary>
    public static void RecordRejected(NotAssessedReason reason) => InvestigationTelemetry.Rejected(reason);
}
