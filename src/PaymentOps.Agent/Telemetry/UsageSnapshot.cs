using System.Collections.Immutable;

using PaymentOps.Agent.Investigating;

namespace PaymentOps.Agent.Telemetry;

/// <summary>What one deployment was asked for and reported in an investigation.</summary>
/// <param name="LogicalCalls">Requests the agent made; one however many HTTP attempts it took.</param>
/// <param name="HttpAttempts">HTTP requests sent, the SDK's retries and failures included.</param>
/// <param name="CachedInputTokens">The part of <paramref name="InputTokens"/> the service served from its prompt cache.</param>
internal sealed record DeploymentUsage(int LogicalCalls, int HttpAttempts, long InputTokens, long CachedInputTokens, long OutputTokens);

/// <summary>
/// Everything one investigation used, frozen when it ends, before its usage scope does: the one source for the
/// decision record, the runtime cost estimate and the evaluation, so nothing is counted twice.
/// </summary>
/// <param name="Routing">The routing mode the agent ran under; null when no attempt ran.</param>
/// <param name="ContentSafetyRecords">Text records answered, by Content Safety service.</param>
/// <param name="HttpRequests">Requests sent, by service.</param>
/// <param name="Escalation"><c>none</c>, <c>escalated</c> or <c>refused:</c> and the bound; null when no attempt ran.</param>
/// <param name="EstimateExceeded">Model calls whose measured input exceeded their pre-call estimate.</param>
internal sealed record UsageSnapshot(
    string? Routing,
    ImmutableSortedDictionary<string, DeploymentUsage> Deployments,
    ImmutableSortedDictionary<string, int> ContentSafetyRecords,
    ImmutableSortedDictionary<string, int> HttpRequests,
    ImmutableSortedDictionary<string, int> CacheHits,
    ImmutableSortedDictionary<string, int> Screens,
    ImmutableArray<(string Name, string Outcome)> Tools,
    ImmutableArray<AttemptRecord> Attempts,
    string? Escalation,
    int EstimateExceeded,
    ImmutableArray<string> Incomplete)
{
    public bool Complete => Incomplete.IsEmpty;

    /// <summary>Reads served from the investigation's own cache instead of the source.</summary>
    public int ReusedReads => CacheHits.GetValueOrDefault(ReadCache.Name);
}

/// <summary>Receives each investigation's usage when it ends. The app registers none; the evaluation records cases with it.</summary>
internal interface IInvestigationUsageSink
{
    void Completed(Guid investigationId, UsageSnapshot usage);
}
