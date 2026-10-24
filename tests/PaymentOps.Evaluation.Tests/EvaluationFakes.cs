using Azure.Core;

using Microsoft.Extensions.AI;

using PaymentOps.Agent;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Running;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Tests;

/// <summary>The app's real registration and tools over the demo data; only the model and Search are scripted.</summary>
internal static class EvaluationFakes
{
    public const string Fabrikam = "fabrikam-pay";
    public const string MissingFile = "fact:missing-file:fabrikam-pay:2026-11-09";
    public const string Incident = "incident:INC-FAB-1109";

    public static readonly AiRelease Release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());

    public static readonly IReadOnlyList<KnowledgeSearchDocument> Corpus =
        IndexedCorpus.FromSourceDirectory(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src"));

    public static readonly RetrievedSection GeneralSection = Section(Corpus.First(s => s.Classification == "general" && s.Content.Length > 100));
    public static readonly RetrievedSection RestrictedSection = Section(Corpus.First(s => s.Classification == "restricted" && s.Content.Length > 100));

    // Never contacted: a scripted model and a fake knowledge lookup stand in for both services.
    public static readonly InvestigationEndpoints Endpoints = new(new Uri("https://search.invalid"), new Uri("https://foundry.invalid"), "https://cognitiveservices.azure.com/.default");

    public static RetrievedSection Section(KnowledgeSearchDocument s) => new(s.Id, s.DocId, s.Title, s.SectionPath, s.Content, s.Classification, null);

    public static InvestigationCase Case(string id = "c1", string role = InvestigationCase.Reader, string access = "general") => new(
        id, "core", access, role, "Why were M-1042's payouts delayed yesterday?", ["assessed"], [ToolNames.MerchantProfile], [],
        ScoreEvidence: false, EvidenceOutcomes: null, ExpectedEvidence: [], RequiredSections: [], AcceptableSections: [],
        Proposal: InvestigationCase.ProposalEither, RestrictedMarkers: [], ExpectedRootCause: null);

    public static AgentCaseRunner AgentRunner(Func<ScriptedChatClient> model, IKnowledgeLookup? knowledge = null) =>
        new(Release, Endpoints, new UnusedCredential(), _ => model(), _ => knowledge ?? new FixedKnowledge(GeneralSection));

    public static ScriptedChatClient Model(params ChatResponse[] responses) =>
        new([.. responses.Select(r => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => r))]);

    public static object Statement(string text, string[] evidence, string[] citations) => new { text, evidence_ids = evidence, citation_ids = citations };

    public static object Ticket() => new
    {
        processor_id = Fabrikam,
        merchant_ids = new[] { "M-1042" },
        start_date = "2026-11-09",
        end_date = "2026-11-09",
        title = "Fabrikam Pay settlement file missing for 9 November",
        summary = "No settlement file arrived from Fabrikam Pay for the 9 November batch.",
        evidence_ids = new[] { MissingFile, Incident },
        citation_ids = new[] { "S1" },
    };

    public static object Submission(object? ticket = null, string outcome = "assessed") => new
    {
        outcome,
        summary = Statement("The 9 November Fabrikam file never arrived.", [MissingFile], ["S1"]),
        claims = new[] { Statement("M-1042 settles through Fabrikam Pay.", ["merchant:M-1042"], []) },
        root_cause = Statement("A Fabrikam settlement outage stopped the file.", [MissingFile, Incident], ["S1"]),
        confidence = "high",
        next_steps = Array.Empty<object>(),
        proposed_ticket = ticket,
    };

    public static ChatResponse[] Trajectory(object submission) =>
    [
        ScriptedChatClient.Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
        ScriptedChatClient.Call(ToolNames.SettlementBatches, new { merchant_id = "M-1042", start_date = "2026-11-09", end_date = "2026-11-10" }),
        ScriptedChatClient.Call(ToolNames.ProcessorStatus, new { processor_id = Fabrikam, start_date = "2026-11-09", end_date = "2026-11-10" }),
        ScriptedChatClient.Call(ToolNames.KnowledgeSearch, new { query = "missing settlement file" }),
        ScriptedChatClient.Call(ToolNames.SubmitAssessment, submission),
    ];

    /// <summary>Fails if anything asks it for a token: no request should leave a test.</summary>
    internal sealed class UnusedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No token should be requested in a test.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No token should be requested in a test.");
    }

    internal sealed class FixedKnowledge(params RetrievedSection[] sections) : IKnowledgeLookup
    {
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken) =>
            Failure is { } failure ? Task.FromException<IReadOnlyList<RetrievedSection>>(failure) : Task.FromResult<IReadOnlyList<RetrievedSection>>(sections);

        public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
            Task.FromResult(sections.FirstOrDefault(s => s.Key == sectionKey));
    }

    /// <summary>Returns the scripted runs in order and counts how often it was asked.</summary>
    internal sealed class ScriptedRunner(params RunOutcome[] runs) : ICaseRunner
    {
        public int Calls { get; private set; }

        public Task<RunOutcome> RunAsync(IReadOnlyList<InvestigationCase> cases, CancellationToken cancellationToken) => Task.FromResult(runs[Calls++]);
    }
}
