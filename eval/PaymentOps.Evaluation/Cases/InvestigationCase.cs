using PaymentOps.Agent.Contracts;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Tools;

namespace PaymentOps.Evaluation.Cases;

/// <summary>
/// One labelled investigation question. Every label is written by a person from the demo data, the runbooks and
/// the role rules; which metrics a case counts towards follows from these labels, never from what a run produced.
/// </summary>
/// <param name="AllowedOutcomes">Outcomes a reviewer accepts: <c>assessed</c>, <c>incomplete</c>, <c>escalated</c>, <c>refused</c> or <c>not-assessed:&lt;reason&gt;</c>.</param>
/// <param name="ScoreEvidence">Whether the case counts towards evidence, section and judge scores.</param>
/// <param name="EvidenceOutcomes">The outcomes whose content earns evidence and section credit; null means assessed and escalated.</param>
/// <param name="ExpectedEvidence">Any-of groups of evidence ids: a group is met when one of its ids is cited.</param>
/// <param name="Proposal"><c>expected</c>, <c>forbidden</c> or <c>either</c>.</param>
/// <param name="RestrictedMarkers">Phrases found only in Restricted sections; none may reach a General user.</param>
/// <param name="ExpectedRootCause">For the judge only; never scored deterministically.</param>
internal sealed record InvestigationCase(
    string Id,
    string Group,
    string Access,
    string Role,
    string Question,
    IReadOnlyList<string> AllowedOutcomes,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<ToolArgumentLabel> ToolArguments,
    bool ScoreEvidence,
    IReadOnlyList<string>? EvidenceOutcomes,
    IReadOnlyList<IReadOnlyList<string>> ExpectedEvidence,
    IReadOnlyList<ExpectedSection> RequiredSections,
    IReadOnlyList<ExpectedSection> AcceptableSections,
    string Proposal,
    IReadOnlyList<string> RestrictedMarkers,
    string? ExpectedRootCause)
{
    public const string Reader = "reader";
    public const string Engineer = "engineer";

    public const string ProposalExpected = "expected";
    public const string ProposalForbidden = "forbidden";
    public const string ProposalEither = "either";

    public const string RegressionGroup = "regression";
    public const string KnowledgeOnlyGroup = "knowledge-only";
    public const string AuthorizationGroup = "authorization";

    public static IReadOnlyList<string> Groups { get; } =
        [RegressionGroup, "core", "decoy", "phrasing", KnowledgeOnlyGroup, "refusal", AuthorizationGroup, "role"];

    public static IReadOnlyList<string> AssessedOutcomes { get; } = [.. Enum.GetValues<AssessmentOutcome>().Select(Outcomes.Of)];

    public static IReadOnlyList<string> AllOutcomes { get; } =
        [.. AssessedOutcomes, .. Enum.GetValues<NotAssessedReason>().Select(Outcomes.NotAssessed)];

    private static readonly IReadOnlyList<string> DefaultEvidenceOutcomes = [Outcomes.Of(AssessmentOutcome.Assessed), Outcomes.Of(AssessmentOutcome.Escalated)];

    public IReadOnlyList<string> CreditableOutcomes => EvidenceOutcomes ?? DefaultEvidenceOutcomes;
}

/// <summary>What one required tool's arguments must satisfy, in at least one of its calls.</summary>
internal sealed record ToolArgumentLabel(string Tool, IReadOnlyList<ArgumentConstraint> Constraints);

/// <param name="Field">The argument as the tool receives it, in snake_case.</param>
/// <param name="Kind"><c>equals</c>: the argument is the value. <c>covers</c> (field <c>date_range</c>): the call's start_date..end_date includes the value's date.</param>
internal sealed record ArgumentConstraint(string Field, string Kind, string Value)
{
    public const string EqualsKind = "equals";
    public const string CoversKind = "covers";
    public const string DateRangeField = "date_range";
}

/// <summary>How a result's outcome is written in labels and reports.</summary>
internal static class Outcomes
{
    public const string NotAssessedPrefix = "not-assessed:";

    /// <summary>A case the run recorded no result for; it scores 0 everywhere.</summary>
    public const string Missing = "missing";

    public static string Of(AssessmentOutcome outcome) => Word(outcome);

    public static string NotAssessed(NotAssessedReason reason) => NotAssessedPrefix + Word(reason);

    public static string Of(AssessmentResult result) => result switch
    {
        AssessmentResult.Assessed a => Of(a.Outcome),
        AssessmentResult.NotAssessed n => NotAssessed(n.Reason),
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    /// <summary>An enum value as labels and reports write it, such as <c>assessment-unverified</c>.</summary>
    public static string Word(Enum value) => ToolJson.Word(value).Replace('_', '-');
}
