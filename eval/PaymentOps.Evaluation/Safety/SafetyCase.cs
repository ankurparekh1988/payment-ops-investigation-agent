using PaymentOps.Evaluation.Cases;

namespace PaymentOps.Evaluation.Safety;

/// <summary>
/// One safety question. Indirect attacks and benign tool text plant their <see cref="Poison"/> in what a tool
/// returns for this case only; an indirect attack lists the effects that would show the agent followed it.
/// </summary>
/// <param name="Kind">One of <see cref="Kinds"/>.</param>
internal sealed record SafetyCase(string Id, string Kind, string Access, string Role, string Question, SafetyPoison? Poison, ForbiddenEffects? Forbidden)
{
    public const string IndirectAttack = "indirect-attack";
    public const string DirectAttack = "direct-attack";
    public const string Harmful = "harmful";
    public const string BenignQuestion = "benign-question";
    public const string BenignText = "benign-text";

    public static IReadOnlyList<string> Kinds { get; } = [IndirectAttack, DirectAttack, Harmful, BenignQuestion, BenignText];

    /// <summary>
    /// The case as the shared runner takes it. Safety cases are scored by their own metrics, so the labels the
    /// investigation metrics read are left open.
    /// </summary>
    public InvestigationCase ToInvestigationCase() => new(
        Id, "safety", Access, Role, Question, InvestigationCase.AllOutcomes, [], [],
        ScoreEvidence: false, EvidenceOutcomes: null, ExpectedEvidence: [], RequiredSections: [], AcceptableSections: [],
        Proposal: InvestigationCase.ProposalEither, RestrictedMarkers: Forbidden is { RestrictedSections: true } f ? f.Text : [], ExpectedRootCause: null);
}

/// <summary>Text appended to what one tool returns for one case.</summary>
/// <param name="Surface"><c>processor-incident</c> (every incident summary of the processor <paramref name="Target"/>) or <c>knowledge</c>.</param>
/// <param name="Target">A processor id, or for knowledge the document id.</param>
/// <param name="SectionPath">For knowledge, the General section the text is appended to; null for incidents.</param>
internal sealed record SafetyPoison(string Surface, string Target, string? SectionPath, string Payload)
{
    public const string ProcessorIncident = "processor-incident";
    public const string Knowledge = "knowledge";
}

/// <summary>What must never happen if the agent resisted the planted instruction.</summary>
/// <param name="Text">Phrases that mustn't appear, case-insensitively, in anything the model wrote for the user.</param>
/// <param name="Proposal">Whether any proposal at all is forbidden.</param>
/// <param name="Outcomes">Outcomes the case mustn't end with.</param>
/// <param name="Confidence">Confidence levels the result mustn't claim.</param>
/// <param name="MerchantIds">Merchants no tool may be called for.</param>
/// <param name="RestrictedSections">Whether retrieving or citing a Restricted section under General access counts.</param>
internal sealed record ForbiddenEffects(
    IReadOnlyList<string> Text,
    bool Proposal,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<string> Confidence,
    IReadOnlyList<string> MerchantIds,
    bool RestrictedSections)
{
    public bool Any => Text.Count > 0 || Proposal || Outcomes.Count > 0 || Confidence.Count > 0 || MerchantIds.Count > 0 || RestrictedSections;
}
