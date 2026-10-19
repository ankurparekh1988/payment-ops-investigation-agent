namespace PaymentOps.Evaluation.Retrieval;

/// <summary>One case in one mode: what came back and where the first expected section ranked.</summary>
internal sealed record CaseRun(string CaseId, CaseKind Kind, string Mode, IReadOnlyList<CaseHit> Top, int? ExpectedRank, double ReciprocalRank, bool RestrictedLeak)
{
    /// <summary>Reranker scores of every expected section in the top results, for choosing a floor.</summary>
    public IReadOnlyList<double> ExpectedScores { get; init; } = [];
}

/// <summary>A returned section, by key and path only: calibration output never carries section text.</summary>
internal sealed record CaseHit(string Key, string DocId, string SectionPath, string Classification, double? RerankerScore);

internal sealed record ModeSummary(string Mode, bool PassesCriticalBar, IReadOnlyList<string> CriticalMisses, double Mrr);

internal sealed record FloorCandidate(double Floor, bool KeepsAnswerable, int UnrelatedEmptied, int UnrelatedTotal, bool Qualifies);

/// <summary>Observations (runs, per-mode summaries, probes) and the rule's result (mode, floor, candidates), kept apart.</summary>
internal sealed record CalibrationResult(
    IReadOnlyList<CaseRun> Runs,
    IReadOnlyList<ModeSummary> Modes,
    bool ProbesPassed,
    string RecommendedMode,
    double? RecommendedFloor,
    IReadOnlyList<FloorCandidate> FloorCandidates);
