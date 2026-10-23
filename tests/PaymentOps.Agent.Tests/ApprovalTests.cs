using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>The approval boundary, one test per threat: only the owning Engineer's click creates a ticket, once.</summary>
public sealed class ApprovalTests : IDisposable
{
    private const string Fabrikam = "fabrikam-pay";
    private const string MissingFile = "fact:missing-file:fabrikam-pay:2026-11-09";
    private const string Incident = "incident:INC-FAB-1109";
    private const string Question = "Why were M-1042's payouts delayed yesterday?";

    // Real time is earlier than the data's as-of date (2026-11-11): the two clocks must never be mixed.
    private static readonly DateTimeOffset RealNow = new(2026, 10, 22, 14, 17, 0, TimeSpan.Zero);
    private static readonly AiRelease Release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());

    private readonly FakeTimeProvider _time = new(RealNow);
    private readonly AgentFixture _fixture;
    private readonly FlakyIncidentStore _tickets;
    private readonly RecordingLogger<ApprovalService> _log = new();
    private ProposalStore _proposals;

    public ApprovalTests()
    {
        _fixture = new AgentFixture(knowledge: new CorpusKnowledge(), time: _time);
        _tickets = new FlakyIncidentStore(_fixture.IncidentStore);
        _proposals = Store();
    }

    public void Dispose() => _fixture.Dispose();

    private ProposalStore Store(int maxPending = 500) =>
        new(_time, Options.Create(new ApprovalOptions { MaxPendingProposals = maxPending }), Release);

    private static object Ticket(string summary = "No settlement file arrived from Fabrikam Pay for the 9 November batch.") => new
    {
        processor_id = Fabrikam,
        merchant_ids = new[] { "M-1042" },
        start_date = "2026-11-09",
        end_date = "2026-11-09",
        title = "Fabrikam Pay settlement file missing for 9 November",
        summary,
        evidence_ids = new[] { MissingFile, Incident },
        citation_ids = new[] { "S1" },
    };

    private static object Statement(string text, string[] evidence, string[] citations) => new { text, evidence_ids = evidence, citation_ids = citations };

    private static object Submission(object? ticket, string summary = "The 9 November Fabrikam file never arrived.") => new
    {
        outcome = "assessed",
        summary = Statement(summary, [MissingFile], ["S1"]),
        claims = new[] { Statement("The runbook says to raise an incident with the processor.", [MissingFile, Incident], ["S1"]) },
        root_cause = Statement("A Fabrikam settlement outage stopped the file.", [MissingFile, Incident], ["S1"]),
        confidence = "high",
        next_steps = Array.Empty<object>(),
        proposed_ticket = ticket,
    };

    private static ChatResponse[] Trajectory(object submission) =>
    [
        Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
        Call(ToolNames.SettlementBatches, new { merchant_id = "M-1042", start_date = "2026-11-09", end_date = "2026-11-10" }),
        Call(ToolNames.ProcessorStatus, new { processor_id = Fabrikam, start_date = "2026-11-09", end_date = "2026-11-10" }),
        Call(ToolNames.KnowledgeSearch, new { query = "payout-delay-processor-outage" }),
        Call(ToolNames.SubmitAssessment, submission),
    ];

    private static ScriptedChatClient Model(params ChatResponse[] responses) =>
        new([.. responses.Select(r => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ => r))]);

    private InvestigationService Investigation(IUserContext user, IChatClient model, Conversation? conversation = null)
    {
        var limits = Options.Create(new InvestigationLimits());
        return new InvestigationService(
            _fixture.Runner(model, user),
            _fixture.FixedPrompt,
            new SourceService(FakeKnowledgeLookup.Returning(), user, limits, NullLogger<SourceService>.Instance),
            conversation ?? new Conversation(),
            Options.Create(new ConversationOptions()),
            limits,
            user,
            _proposals);
    }

    private ApprovalService Approvals(IUserContext user) => new(_proposals, _tickets, user, _log);

    private async Task<TicketProposal> ProposeAsync(IUserContext user, Conversation? conversation = null)
    {
        var result = await Investigation(user, Model(Trajectory(Submission(Ticket()))), conversation).InvestigateAsync(Question, null, CancellationToken.None);
        return Assert.IsType<AssessmentResult.Assessed>(result).Proposal!;
    }

    private Task<IncidentTicket?> Ticket(int number) => _tickets.FindAsync($"INC-{number:D4}", CancellationToken.None);

    [Fact]
    public async Task A_readers_assessment_creates_no_pending_proposal()
    {
        var proposal = await ProposeAsync(TestUser.Reader);

        Assert.Equal(ProposalStatus.Draft, proposal.Status);
        Assert.Null(proposal.ProposalId);
        Assert.Equal(0, _proposals.PendingCount);
    }

    [Fact]
    public async Task An_engineers_assessment_creates_a_pending_proposal_and_no_ticket()
    {
        var proposal = await ProposeAsync(TestUser.Engineer);

        Assert.Equal(ProposalStatus.Pending, proposal.Status);
        Assert.NotNull(proposal.ProposalId);
        Assert.Equal(RealNow.AddMinutes(15), proposal.ExpiresAt);
        Assert.Equal("FAB-7731", proposal.ProcessorReference);
        Assert.Equal(1, _proposals.PendingCount);
        Assert.Null(await Ticket(1));
    }

    [Fact]
    public async Task The_owner_approves_and_one_ticket_is_created_from_the_stored_proposal()
    {
        var proposal = await ProposeAsync(TestUser.Engineer);

        var created = Assert.IsType<ApprovalResult.Created>(await Approvals(TestUser.Engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));

        var ticket = (await Ticket(1))!;
        Assert.Equal("INC-0001", created.TicketId);
        Assert.Equal(proposal.Title, ticket.Content.Title);
        Assert.Equal("FAB-7731", ticket.Content.ProcessorReference);
        Assert.Equal(new DateOnly(2026, 11, 9), ticket.Content.StartDate);
        Assert.Equal(TestUser.Engineer.UserId, ticket.Content.CreatedBy);
        Assert.Equal([MissingFile, Incident], ticket.Content.EvidenceIds);
        Assert.Equal(RealNow, ticket.CreatedAt);
    }

    [Fact]
    public async Task A_reader_cannot_approve_any_id()
    {
        var proposal = await ProposeAsync(TestUser.Engineer);

        Assert.IsType<ApprovalResult.NotAuthorized>(await Approvals(TestUser.Reader).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
        Assert.IsType<ApprovalResult.NotAuthorized>(await Approvals(TestUser.Reader).ApproveAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<ApprovalResult.NotAuthorized>(await Approvals(TestUser.Anonymous).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
        Assert.Null(await Ticket(1));
    }

    [Fact]
    public async Task Another_engineer_gets_not_found()
    {
        var proposal = await ProposeAsync(TestUser.Engineer);

        Assert.IsType<ApprovalResult.NotFound>(await Approvals(TestUser.OtherEngineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
        Assert.IsType<ApprovalResult.NotFound>(await Approvals(TestUser.OtherEngineer).RejectAsync(proposal.ProposalId!.Value, RejectionReason.Other, null, CancellationToken.None));
        Assert.IsType<ApprovalResult.NotFound>(await Approvals(TestUser.Engineer).ApproveAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await Ticket(1));
    }

    [Fact]
    public async Task Fifty_parallel_approvals_make_one_ticket()
    {
        var id = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => Approvals(TestUser.Engineer).ApproveAsync(id, CancellationToken.None))));

        Assert.Single(results.OfType<ApprovalResult.Created>());
        Assert.All(results, r => Assert.True(r is ApprovalResult.Created or ApprovalResult.AlreadyDecided or ApprovalResult.Unavailable));
        Assert.NotNull(await Ticket(1));
        Assert.Null(await Ticket(2));
    }

    [Fact]
    public async Task Approve_after_a_decision_creates_nothing()
    {
        var approved = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;
        await Approvals(TestUser.Engineer).ApproveAsync(approved, CancellationToken.None);
        var rejected = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;
        Assert.IsType<ApprovalResult.Rejected>(await Approvals(TestUser.Engineer).RejectAsync(rejected, RejectionReason.NotAnIncident, "Closed late, not missing.", CancellationToken.None));

        Assert.Equal(new ApprovalResult.AlreadyDecided("INC-0001"), await Approvals(TestUser.Engineer).ApproveAsync(approved, CancellationToken.None));
        Assert.Equal(new ApprovalResult.AlreadyDecided(null), await Approvals(TestUser.Engineer).ApproveAsync(rejected, CancellationToken.None));
        Assert.Equal(new ApprovalResult.AlreadyDecided("INC-0001"), await Approvals(TestUser.Engineer).RejectAsync(approved, RejectionReason.Other, null, CancellationToken.None));
        Assert.Null(await Ticket(2));
    }

    // A click while the first is still creating the ticket hasn't been decided either way.
    [Fact]
    public async Task A_second_approval_while_the_first_runs_is_unavailable_not_rejected()
    {
        var id = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;
        _tickets.Hold = new TaskCompletionSource();

        var first = Approvals(TestUser.Engineer).ApproveAsync(id, CancellationToken.None);
        var second = await Approvals(TestUser.Engineer).ApproveAsync(id, CancellationToken.None);
        _tickets.Hold.SetResult();

        Assert.IsType<ApprovalResult.Unavailable>(second);
        Assert.IsType<ApprovalResult.Created>(await first);
    }

    // A new investigation leaves the old card behind, so its proposal stops holding a place in the store.
    [Fact]
    public async Task Starting_a_new_investigation_supersedes_the_pending_proposal()
    {
        var conversation = new Conversation();
        var id = (await ProposeAsync(TestUser.Engineer, conversation)).ProposalId!.Value;

        Investigation(TestUser.Engineer, Model(), conversation).StartNew();

        Assert.Equal(0, _proposals.PendingCount);
        Assert.IsType<ApprovalResult.Superseded>(await Approvals(TestUser.Engineer).ApproveAsync(id, CancellationToken.None));
    }

    // The card goes when the next question is asked, so its proposal can't be left waiting for a decision.
    [Fact]
    public async Task A_follow_up_question_supersedes_the_pending_proposal()
    {
        var conversation = new Conversation();
        var proposal = await ProposeAsync(TestUser.Engineer, conversation);
        var followUp = Model(Call(ToolNames.SubmitAssessment, Submission(null)));

        await Investigation(TestUser.Engineer, followUp, conversation).InvestigateAsync("And what about M-1042?", null, CancellationToken.None);

        Assert.Equal(0, _proposals.PendingCount);
        Assert.IsType<ApprovalResult.Superseded>(await Approvals(TestUser.Engineer).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
    }

    [Fact]
    public async Task A_superseded_proposal_cannot_be_approved()
    {
        var conversation = new Conversation();
        var first = (await ProposeAsync(TestUser.Engineer, conversation)).ProposalId!.Value;
        var second = (await ProposeAsync(TestUser.Engineer, conversation)).ProposalId!.Value;

        Assert.IsType<ApprovalResult.Superseded>(await Approvals(TestUser.Engineer).ApproveAsync(first, CancellationToken.None));
        Assert.IsType<ApprovalResult.Created>(await Approvals(TestUser.Engineer).ApproveAsync(second, CancellationToken.None));
        Assert.Equal(0, _proposals.PendingCount);
        Assert.Null(await Ticket(2));
    }

    [Fact]
    public async Task Expiry_follows_real_time_not_the_investigation_clock()
    {
        var expiring = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;
        _time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.IsType<ApprovalResult.Expired>(await Approvals(TestUser.Engineer).ApproveAsync(expiring, CancellationToken.None));

        // Real time is three weeks before the data's today; within its window the proposal is approvable.
        var fresh = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;
        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.IsType<ApprovalResult.Created>(await Approvals(TestUser.Engineer).ApproveAsync(fresh, CancellationToken.None));
        Assert.Equal(0, _proposals.PendingCount);
    }

    [Fact]
    public void The_approval_service_does_not_depend_on_the_investigation_clock()
    {
        var parameters = typeof(ApprovalService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
            .Concat(typeof(ProposalStore).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));

        Assert.DoesNotContain(typeof(IInvestigationClock), parameters);
        Assert.Contains(typeof(TimeProvider), parameters);
    }

    [Fact]
    public async Task Role_removed_before_the_click_is_not_authorized()
    {
        var proposal = await ProposeAsync(TestUser.Engineer);
        var demoted = TestUser.Engineer with { Roles = OpsRoles.Effective([OpsRoles.Reader]) };

        Assert.IsType<ApprovalResult.NotAuthorized>(await Approvals(demoted).ApproveAsync(proposal.ProposalId!.Value, CancellationToken.None));
        Assert.Null(await Ticket(1));
    }

    [Fact]
    public async Task Model_text_claiming_approval_creates_nothing()
    {
        var model = Model(Trajectory(Submission(Ticket(), summary: "The ticket has been approved and created with the processor.")));

        var result = await Investigation(TestUser.Engineer, model).InvestigateAsync(Question, null, CancellationToken.None);

        Assert.Equal(ProposalStatus.Pending, Assert.IsType<AssessmentResult.Assessed>(result).Proposal!.Status);
        Assert.Null(await Ticket(1));
    }

    [Fact]
    public async Task A_call_to_create_incident_ticket_ends_unverified_with_an_empty_store()
    {
        var model = Model(Call("create_incident_ticket", new { processor_id = Fabrikam }));

        var result = await Investigation(TestUser.Engineer, model).InvestigateAsync(Question, null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Equal(0, _proposals.PendingCount);
        Assert.Null(await Ticket(1));
    }

    // Review focus: a cancel that lands after the submission was accepted still keeps nothing.
    [Fact]
    public async Task A_cancelled_turn_materializes_nothing()
    {
        using var cancel = new CancellationTokenSource();
        var conversation = new Conversation();
        var steps = Trajectory(Submission(Ticket()));
        var model = new ScriptedChatClient([.. steps.Select((r, i) => (Func<IReadOnlyList<ChatMessage>, ChatResponse>)(_ =>
        {
            if (i == steps.Length - 1)
            {
                cancel.Cancel();
            }

            return r;
        }))]);

        var result = await Investigation(TestUser.Engineer, model, conversation).InvestigateAsync(Question, null, cancel.Token);

        Assert.Equal(NotAssessedReason.Cancelled, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Equal(0, _proposals.PendingCount);
        Assert.Empty(conversation.Turns);
    }

    // Review focus: the store failing mid-approval puts the proposal back, and a retry still makes one ticket.
    [Fact]
    public async Task A_store_failure_returns_the_proposal_to_pending()
    {
        var id = (await ProposeAsync(TestUser.Engineer)).ProposalId!.Value;
        _tickets.FailNext = true;

        Assert.IsType<ApprovalResult.Unavailable>(await Approvals(TestUser.Engineer).ApproveAsync(id, CancellationToken.None));
        Assert.IsType<ApprovalResult.Created>(await Approvals(TestUser.Engineer).ApproveAsync(id, CancellationToken.None));
        Assert.Null(await Ticket(2));
    }

    // The store won't take another ticket until a restart, so retrying can't help: the proposal ends where it is.
    [Fact]
    public async Task A_full_ticket_store_ends_the_proposal_and_creates_nothing()
    {
        var store = new InMemoryIncidentStore(_time, maxTickets: 1);
        var approvals = new ApprovalService(_proposals, store, TestUser.Engineer, _log);
        var first = (await ProposeAsync(TestUser.Engineer, new Conversation())).ProposalId!.Value;
        var second = (await ProposeAsync(TestUser.Engineer, new Conversation())).ProposalId!.Value;
        Assert.IsType<ApprovalResult.Created>(await approvals.ApproveAsync(first, CancellationToken.None));
        Assert.Equal(1, _proposals.PendingCount);

        Assert.IsType<ApprovalResult.CapacityReached>(await approvals.ApproveAsync(second, CancellationToken.None));
        Assert.IsType<ApprovalResult.CapacityReached>(await approvals.ApproveAsync(second, CancellationToken.None));
        Assert.IsType<ApprovalResult.CapacityReached>(await approvals.RejectAsync(second, RejectionReason.Other, null, CancellationToken.None));

        Assert.Equal(0, _proposals.PendingCount);
        Assert.Null(await store.FindAsync("INC-0002", CancellationToken.None));
    }

    // Review focus: with no room for another pending proposal the assessment still shows.
    [Fact]
    public async Task A_full_store_shows_the_assessment_with_the_proposal_unavailable()
    {
        _proposals = Store(maxPending: 1);
        await ProposeAsync(TestUser.Engineer);

        var proposal = await ProposeAsync(TestUser.Engineer);

        Assert.Equal(ProposalStatus.Unavailable, proposal.Status);
        Assert.Null(proposal.ProposalId);
        Assert.Equal(1, _proposals.PendingCount);
    }

    [Fact]
    public async Task The_decision_log_holds_ids_only()
    {
        var approved = await ProposeAsync(TestUser.Engineer);
        await Approvals(TestUser.Engineer).ApproveAsync(approved.ProposalId!.Value, CancellationToken.None);
        var rejected = await ProposeAsync(TestUser.Engineer);
        await Approvals(TestUser.Engineer).RejectAsync(rejected.ProposalId!.Value, RejectionReason.EvidenceDoesNotSupportIt, "secret note text", CancellationToken.None);

        Assert.Equal(2, _log.Entries.Count);
        Assert.Contains("INC-0001", _log.Entries[0].Message, StringComparison.Ordinal);
        Assert.Contains(approved.ProposalId!.Value.ToString(), _log.Entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("EvidenceDoesNotSupportIt", _log.Entries[1].Message, StringComparison.Ordinal);
        foreach (var (message, _) in _log.Entries)
        {
            Assert.DoesNotContain(approved.Title, message, StringComparison.Ordinal);
            Assert.DoesNotContain("No settlement file", message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret note text", message, StringComparison.Ordinal);
            Assert.DoesNotContain(Question, message, StringComparison.Ordinal);
            Assert.DoesNotContain(TestUser.Engineer.UserId, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_next_turn_learns_the_outcome()
    {
        var conversation = new Conversation();
        var approved = await ProposeAsync(TestUser.Engineer, conversation);
        await Approvals(TestUser.Engineer).ApproveAsync(approved.ProposalId!.Value, CancellationToken.None);
        Assert.Contains("Ticket INC-0001 was created.", History(conversation), StringComparison.Ordinal);

        var expiring = new Conversation();
        await ProposeAsync(TestUser.Engineer, expiring);
        _time.Advance(TimeSpan.FromMinutes(16));
        Assert.Contains("The proposed ticket expired unapproved.", History(expiring), StringComparison.Ordinal);
    }

    private string History(Conversation conversation) =>
        string.Join("\n", HistoryProjection.Build(conversation.Turns, 100_000, _proposals.NoteFor).Select(m => m.Text));

    private sealed class TransientStoreException : Exception;

    /// <summary>The real store, failing once on request.</summary>
    private sealed class FlakyIncidentStore(IIncidentStore inner) : IIncidentStore
    {
        public bool FailNext { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        public async Task<IncidentTicket> CreateAsync(string idempotencyKey, IncidentTicketContent content, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new TransientStoreException();
            }

            if (Hold is { } hold)
            {
                await hold.Task;
            }

            return await inner.CreateAsync(idempotencyKey, content, cancellationToken);
        }

        public Task<IncidentTicket?> FindAsync(string ticketId, CancellationToken cancellationToken) => inner.FindAsync(ticketId, cancellationToken);
    }
}
