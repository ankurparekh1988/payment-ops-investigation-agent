namespace PaymentOps.Evaluation.Cases;

/// <summary>The metric names exactly as reports, thresholds and the decision rules use them.</summary>
internal static class MetricNames
{
    public const string AuthorizationLeaks = "authorization_leaks";
    public const string UnauthorizedStateChanges = "unauthorized_state_changes";

    public const string ValidatedResultRate = "validated_result_rate";
    public const string AllowedOutcomeRate = "allowed_outcome_rate";
    public const string RequiredToolRecall = "required_tool_recall";
    public const string ToolArgumentAccuracy = "tool_argument_accuracy";
    public const string ExpectedEvidenceCoverage = "expected_evidence_coverage";
    public const string ExpectedSectionPrecision = "expected_section_precision";
    public const string ExpectedSectionRecall = "expected_section_recall";
    public const string ProposalFalsePositiveRate = "proposal_false_positive_rate";
    public const string RetrievalRecallAt5 = "retrieval_recall_at_5";

    public const string ProposalRecall = "proposal_recall";
    public const string RetrievalMrr = "retrieval_mrr";

    /// <summary>The investigation metrics, gating and reported, in report order.</summary>
    public static IReadOnlyList<string> Investigation { get; } =
    [
        ValidatedResultRate, AllowedOutcomeRate, RequiredToolRecall, ToolArgumentAccuracy, ExpectedEvidenceCoverage,
        ExpectedSectionPrecision, ExpectedSectionRecall, ProposalFalsePositiveRate, ProposalRecall,
    ];
}

/// <summary>
/// N for each investigation metric: the opportunities the frozen labels define, counted before any run and the
/// same in every run. A metric is n/a only when this is 0.
/// </summary>
internal static class Opportunities
{
    public static IReadOnlyDictionary<string, int> Count(IReadOnlyList<InvestigationCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var eligible = cases.Where(c => c.ScoreEvidence).ToList();
        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [MetricNames.ValidatedResultRate] = cases.Count,
            [MetricNames.AllowedOutcomeRate] = cases.Count,
            [MetricNames.RequiredToolRecall] = cases.Sum(c => c.RequiredTools.Distinct().Count()),
            [MetricNames.ToolArgumentAccuracy] = cases.Sum(c => c.ToolArguments.Count),
            [MetricNames.ExpectedEvidenceCoverage] = eligible.Sum(c => c.ExpectedEvidence.Count),
            [MetricNames.ExpectedSectionPrecision] = eligible.Count(c => c.AcceptableSections.Count > 0),
            [MetricNames.ExpectedSectionRecall] = eligible.Sum(c => c.RequiredSections.Distinct().Count()),
            [MetricNames.ProposalFalsePositiveRate] = cases.Count(c => c.Proposal == InvestigationCase.ProposalForbidden),
            [MetricNames.ProposalRecall] = cases.Count(c => c.Proposal == InvestigationCase.ProposalExpected),
        };
    }
}
