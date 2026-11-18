using System.Text.Json.Serialization;

using PaymentOps.Agent.Safety;

namespace PaymentOps.Evaluation.Running;

/// <summary>One untrusted value the tool-text screen checked, with its verdict; held in memory for scoring only.</summary>
internal sealed record ScreenedText(string Text, ScreenVerdict Verdict);

/// <param name="Arguments">The arguments as the tool received them, by snake_case name: ids, dates and queries.</param>
internal sealed record ToolCallRecord(string Tool, IReadOnlyDictionary<string, string?> Arguments)
{
    /// <summary>The deployment whose model made the call; null when it wasn't known.</summary>
    public string? Deployment { get; init; }
}

/// <summary>A section a knowledge search returned, by key and classification only.</summary>
internal sealed record SectionHit(string Key, string Classification);

/// <summary>One deployment's part of a case: logical calls, HTTP attempts (retries included) and tokens.</summary>
internal sealed record DeploymentTokens(int LogicalCalls, int HttpAttempts, long InputTokens, long CachedInputTokens, long OutputTokens);

/// <summary>What the app recorded the case's investigation used, from its one usage snapshot.</summary>
/// <param name="Deployments">By deployment name, the classifier's included.</param>
/// <param name="ContentSafetyRecords">Text records answered, by Content Safety service.</param>
/// <param name="Incomplete">Why usage can't be fully known; empty when it can.</param>
/// <param name="EstimateExceeded">Model calls whose measured input exceeded their pre-call estimate.</param>
internal sealed record CaseUsage(
    IReadOnlyDictionary<string, DeploymentTokens> Deployments,
    IReadOnlyDictionary<string, int> ContentSafetyRecords,
    IReadOnlyList<string> Incomplete,
    int EstimateExceeded);

/// <summary>One model attempt: its tier and why it failed, or null for the one that gave a validated result.</summary>
internal sealed record AttemptSummary(string Tier, string? Failure);

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
    /// <summary>Every tool call of every attempt: what touched data, as the safety checks count it.</summary>
    public IReadOnlyList<ToolCallRecord> Calls { get; init; } = [];

    /// <summary>The deployment of the case's last model request: the attempt whose result the case is.</summary>
    public string? AnsweringDeployment { get; init; }

    /// <summary>
    /// The calls of the attempt whose result the case is, as tool recall and arguments are scored: a discarded attempt's
    /// calls earn the answer nothing. With one deployment, every call.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ToolCallRecord> AnsweringCalls => Calls.Where(call => call.Deployment == AnsweringDeployment);

    public IReadOnlyList<string> AssessmentEvidenceIds { get; init; } = [];

    public IReadOnlyList<string> AssessmentSectionKeys { get; init; } = [];

    public IReadOnlyList<string> ProposalEvidenceIds { get; init; } = [];

    public IReadOnlyList<string> ProposalSectionKeys { get; init; } = [];

    public IReadOnlyList<SectionHit> RetrievedSections { get; init; } = [];

    public string ProposalState { get; init; } = ProposalStates.None;

    /// <summary>The confidence an assessed result claimed, as labels write it; null when not assessed.</summary>
    public string? Confidence { get; init; }

    /// <summary>Tool text the screen flagged as an instruction, and so withheld from the model.</summary>
    public int WithheldFlagged { get; init; }

    /// <summary>Tool text the screen found harmful, and so withheld from the model.</summary>
    public int WithheldHarmful { get; init; }

    /// <summary>Every value the tool-text screen checked, with its verdict, so a case is scored on its own planted text.</summary>
    [JsonIgnore]
    public IReadOnlyList<ScreenedText> Screened { get; init; } = [];

    /// <summary>Set when the evaluation environment, not the system under test, failed this attempt; the attempt is discarded and never scored.</summary>
    public string? InfrastructureFailure { get; init; }

    /// <summary>
    /// The question screen couldn't check the first ask, so the question was asked once more and this is the second
    /// ask's result. The agent never saw the first ask.
    /// </summary>
    public bool Reasked { get; init; }

    public long LatencyMs { get; init; }

    /// <summary>The case's usage as the app recorded it; null when no investigation reported any.</summary>
    public CaseUsage? Usage { get; init; }

    public IReadOnlyList<AttemptSummary> Attempts { get; init; } = [];

    /// <summary><c>none</c>, <c>escalated</c> or <c>refused:</c> and the bound; null when no attempt ran.</summary>
    public string? Escalation { get; init; }

    /// <summary>Reads served to an escalated attempt from the investigation's own cache.</summary>
    public int ReusedReads { get; init; }

    public long? TtfpMs { get; init; }

    public int ModelRequests { get; init; }

    /// <summary>The investigation's model tokens, every deployment but the screen's; from <see cref="Usage"/>.</summary>
    public long InputTokens { get; init; }

    /// <summary>The part of <see cref="InputTokens"/> the service served from its prompt cache, billed at a lower rate.</summary>
    public long CachedInputTokens { get; init; }

    public long OutputTokens { get; init; }

    /// <summary>Summary, claims, root cause and next steps: what the judge reads.</summary>
    [JsonIgnore]
    public string AssessmentText { get; init; } = string.Empty;

    /// <summary>The model's knowledge queries, for the safety scan only; the model's words never enter a report.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> KnowledgeQueries { get; init; } = [];

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

/// <summary>One case's result and the tickets its own services' store holds at the end.</summary>
internal sealed record CaseAttempt(CaseResult Result, int Tickets);

/// <summary>One run: every case's result and the tickets the run's store holds at the end.</summary>
internal sealed record RunOutcome(IReadOnlyList<CaseResult> Results, int TicketsInStore);
