using System.Text.Json.Serialization;

namespace PaymentOps.Evaluation.Running;

/// <param name="Arguments">The arguments as the tool received them, by snake_case name: ids, dates and queries.</param>
internal sealed record ToolCallRecord(string Tool, IReadOnlyDictionary<string, string?> Arguments);

/// <summary>A section a knowledge search returned, by key and classification only.</summary>
internal sealed record SectionHit(string Key, string Classification);

/// <summary>What became of a proposal, from the server's own states.</summary>
internal static class ProposalStates
{
    public const string None = "none";
    public const string Draft = "draft";
    public const string Pending = "pending";

    /// <summary>Proposed, but not shown because the investigation ended incomplete.</summary>
    public const string Withheld = "withheld";

    /// <summary>The proposal store was full, so it couldn't be kept.</summary>
    public const string Unavailable = "unavailable";
    public const string Approved = "approved";
    public const string ApprovalFailed = "approval-failed";
    public const string CapacityReached = "capacity-reached";

    /// <summary>States in which a proposal is held on the server, which only an Engineer's may be.</summary>
    public static IReadOnlySet<string> Stored { get; } = new HashSet<string>(StringComparer.Ordinal) { Pending, Approved, ApprovalFailed, CapacityReached };
}

/// <summary>
/// What one case produced in one run, recorded for scoring. Assessment and proposal support are kept apart: only
/// the assessment's statements earn evidence and section credit. Model text and fact bodies are held for the
/// judge and the leak scan in memory only, and never serialized.
/// </summary>
/// <param name="Outcome">In the labels' vocabulary: <c>assessed</c>, <c>incomplete</c>, ... or <c>not-assessed:model-unavailable</c>.</param>
internal sealed record CaseResult(string CaseId, string Outcome)
{
    public IReadOnlyList<ToolCallRecord> Calls { get; init; } = [];

    public IReadOnlyList<string> AssessmentEvidenceIds { get; init; } = [];

    public IReadOnlyList<string> AssessmentSectionKeys { get; init; } = [];

    public IReadOnlyList<string> ProposalEvidenceIds { get; init; } = [];

    public IReadOnlyList<string> ProposalSectionKeys { get; init; } = [];

    public IReadOnlyList<SectionHit> RetrievedSections { get; init; } = [];

    public string ProposalState { get; init; } = ProposalStates.None;

    /// <summary>Set when the evaluation environment, not the system under test, failed this case; it invalidates the run.</summary>
    public string? InfrastructureFailure { get; init; }

    public long LatencyMs { get; init; }

    public long? TtfpMs { get; init; }

    public int ModelRequests { get; init; }

    public long InputTokens { get; init; }

    /// <summary>The part of <see cref="InputTokens"/> the service served from its prompt cache, billed at a lower rate.</summary>
    public long CachedInputTokens { get; init; }

    public long OutputTokens { get; init; }

    /// <summary>Summary, claims, root cause and next steps: what the judge reads.</summary>
    [JsonIgnore]
    public string AssessmentText { get; init; } = string.Empty;

    /// <summary>Everything the user is shown that the model wrote, the proposed ticket included: what the leak scan reads.</summary>
    [JsonIgnore]
    public string UserVisibleText { get; init; } = string.Empty;

    /// <summary>Evidence id to the typed data the tools returned for it, for the assessment's cited ids.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> CitedEvidenceFacts { get; init; } = new Dictionary<string, string>();

    /// <summary>Section key to its text, for the sections the assessment cited.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> CitedSectionText { get; init; } = new Dictionary<string, string>();
}

/// <summary>One run: every case's result and the tickets the run's store holds at the end.</summary>
internal sealed record RunOutcome(IReadOnlyList<CaseResult> Results, int TicketsInStore);
