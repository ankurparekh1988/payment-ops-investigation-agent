using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Investigating;

namespace PaymentOps.Web.SmokeTests.Investigating;

/// <summary>
/// The session holds what the Investigate page shows and the run behind it, so both outlive the page. It never decides
/// anything the server owns: a card is actionable only while the stored proposal is pending and unexpired.
/// </summary>
public sealed class InvestigationSessionTests
{
    private readonly ScriptedInvestigationService _investigation = new();
    private readonly CountingApprovalService _approvals = new(new ApprovalResult.Created("INC-0042"));
    private readonly FakeTimeProvider _time = new(Results.Now);

    private InvestigationSession Session() => new(_investigation, _approvals, _time);

    [Fact]
    public async Task A_finished_run_becomes_the_current_result_and_a_turn()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();

        await session.StartAsync("Why was M-1042 not paid?");

        Assert.False(session.Busy);
        var turn = Assert.Single(session.Questions);
        Assert.Equal("Why was M-1042 not paid?", turn.Question);
        Assert.Same(turn.Result, session.Current);
        Assert.Equal(string.Empty, session.Draft);
    }

    [Fact]
    public async Task Disposing_the_session_cancels_the_run()
    {
        var session = Session();
        _investigation.Hold = true;

        var run = session.StartAsync("Why was M-1042 not paid?");
        Assert.True(session.Busy);
        await session.DisposeAsync();

        Assert.True(_investigation.LastToken.IsCancellationRequested);
        await run;
    }

    [Fact]
    public async Task Cancel_ends_the_run_as_cancelled()
    {
        await using var session = Session();
        _investigation.Hold = true;

        var run = session.StartAsync("Why was M-1042 not paid?");
        session.Cancel();
        await run;

        Assert.Equal(NotAssessedReason.Cancelled, Assert.IsType<AssessmentResult.NotAssessed>(session.Current).Reason);
        Assert.False(session.Busy);
    }

    [Fact]
    public async Task Cancel_after_the_run_finished_does_nothing()
    {
        await using var session = Session();

        await session.StartAsync("Why was M-1042 not paid?");
        session.Cancel();

        Assert.False(session.Busy);
    }

    [Fact]
    public async Task A_late_progress_event_after_disposal_is_ignored()
    {
        var session = Session();
        _investigation.Hold = true;
        var run = session.StartAsync("Why was M-1042 not paid?");
        var progress = _investigation.LastProgress!;

        await session.DisposeAsync();
        await run;
        progress.Report(new InvestigationProgress.ToolStarted("get_payout", "Payout P-1"));

        Assert.Empty(session.Steps);
    }

    [Fact]
    public async Task A_progress_event_from_a_previous_run_is_ignored()
    {
        await using var session = Session();
        await session.StartAsync("First question");
        var stale = _investigation.LastProgress!;
        _investigation.Hold = true;
        var second = session.StartAsync("Second question");

        stale.Report(new InvestigationProgress.ToolStarted("get_payout", "Payout from the first run"));
        _investigation.LastProgress!.Report(new InvestigationProgress.ToolStarted("get_payout", "Payout from the second run"));

        var step = Assert.Single(session.Steps);
        Assert.Equal("Payout from the second run", Assert.IsType<InvestigationProgress.ToolStarted>(step).Label);
        session.Cancel();
        await second;
    }

    [Fact]
    public async Task Progress_raises_changed()
    {
        await using var session = Session();
        _investigation.Hold = true;
        var changes = 0;
        session.Changed += () => changes++;
        var run = session.StartAsync("Why was M-1042 not paid?");
        var before = changes;

        _investigation.LastProgress!.Report(new InvestigationProgress.Submitting());

        Assert.Equal(before + 1, changes);
        session.Cancel();
        await run;
    }

    [Fact]
    public async Task A_new_question_supersedes_the_pending_card_of_the_previous_turn()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("First question");

        await session.StartAsync("Second question");

        var first = Assert.IsType<AssessmentResult.Assessed>(session.Questions[0].Result);
        Assert.Equal(ProposalStatus.Superseded, first.Proposal!.Status);
        Assert.Equal(ProposalStatus.Pending, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.Status);
    }

    [Fact]
    public async Task New_investigation_clears_the_page_and_starts_a_new_conversation()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("First question");
        session.Draft = "half-typed";

        session.StartNew();

        Assert.Null(session.Current);
        Assert.Empty(session.Questions);
        Assert.Empty(session.Steps);
        Assert.Equal(string.Empty, session.Draft);
        Assert.Equal(1, _investigation.StartNewCalls);
    }

    [Fact]
    public async Task New_investigation_is_ignored_while_running()
    {
        await using var session = Session();
        _investigation.Hold = true;
        var run = session.StartAsync("Why was M-1042 not paid?");

        session.StartNew();

        Assert.True(session.Busy);
        Assert.Equal(0, _investigation.StartNewCalls);
        session.Cancel();
        await run;
    }

    [Fact]
    public async Task An_expired_pending_card_is_not_actionable_on_return()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");
        var proposal = Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!;
        Assert.True(session.IsActionable(proposal));

        _time.Advance(TimeSpan.FromMinutes(16));

        Assert.False(session.IsActionable(proposal));
        Assert.Equal(ProposalStatus.Expired, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.Status);
        await session.ApproveAsync();
        Assert.Equal(0, _approvals.Approvals);
    }

    [Fact]
    public async Task Approving_shows_what_the_server_answered()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");

        await session.ApproveAsync();
        await session.ApproveAsync();

        var proposal = Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!;
        Assert.Equal(ProposalStatus.Approved, proposal.Status);
        Assert.Equal("INC-0042", proposal.TicketId);
        Assert.Equal(1, _approvals.Approvals);
        Assert.Same(session.Current, session.Questions[^1].Result);
    }

    [Fact]
    public async Task A_failed_decision_leaves_the_card_pending_and_says_so()
    {
        var approvals = new CountingApprovalService(new ApprovalResult.Unavailable());
        await using var session = new InvestigationSession(_investigation, approvals, _time);
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");

        await session.RejectAsync(RejectionReason.NotAnIncident, null);

        Assert.True(session.DecisionFailed);
        Assert.Equal(ProposalStatus.Pending, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.Status);
    }

    [Fact]
    public async Task A_blank_question_or_an_ended_conversation_does_not_run()
    {
        await using var session = Session();

        await session.StartAsync("   ");

        Assert.Empty(_investigation.Asked);
        Assert.Null(session.Current);
    }

    // The server supersedes a proposal only when a run starts; a refusal before that leaves it pending.
    [Fact]
    public async Task A_refusal_before_the_run_keeps_the_pending_card_and_shows_a_notice()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("First question");
        _investigation.Answer = _ => new AssessmentResult.NotAssessed(NotAssessedReason.Paused);

        await session.StartAsync("Second question");

        Assert.Single(session.Questions);
        Assert.Equal(new SessionNotice(NotAssessedReason.Paused, "Second question"), session.Notice);
        Assert.Equal("Second question", session.Draft);
        Assert.Equal(ProposalStatus.Pending, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.Status);
        await session.ApproveAsync();
        Assert.Equal(1, _approvals.Approvals);
    }

    [Fact]
    public async Task A_run_that_reaches_the_server_clears_the_notice()
    {
        await using var session = Session();
        _investigation.Answer = _ => new AssessmentResult.NotAssessed(NotAssessedReason.DailyLimitReached);
        await session.StartAsync("First question");
        _investigation.Answer = _ => Results.WithPendingTicket();

        await session.StartAsync("Second question");

        Assert.Null(session.Notice);
        Assert.Single(session.Questions);
    }

    [Fact]
    public async Task The_conversation_is_read_when_it_can_change_not_on_every_render()
    {
        await using var session = Session();
        _investigation.Hold = true;
        var run = session.StartAsync("Why was M-1042 not paid?");
        var reads = _investigation.ConversationReads;

        for (var i = 0; i < 20; i++)
        {
            _investigation.LastProgress!.Report(new InvestigationProgress.Submitting());
            _ = session.Conversation;
            _ = session.CanStart("another");
        }

        Assert.Equal(reads, _investigation.ConversationReads);
        session.Cancel();
        await run;
    }
}
