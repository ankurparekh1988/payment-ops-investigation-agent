using Microsoft.Extensions.Logging.Abstractions;
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

    private readonly RecordingLogger<InvestigationSession> _log = new();

    private readonly SwitchableUserContext _user = new();

    private InvestigationSession Session() => new(_investigation, _approvals, _user, _time, _log);

    // A failure is shown like a refusal before the run: the earlier result stays under its own question. Whether the
    // failed run got far enough to supersede the earlier proposal isn't known, so it's no longer offered for a decision.
    private void AssertFailedOver(InvestigationSession session, string failed, string previous, AssessmentResult.Assessed previousResult)
    {
        Assert.False(session.Busy);
        Assert.Equal(new SessionNotice(NotAssessedReason.ModelUnavailable, failed), session.Notice);
        Assert.Equal(previous, session.CurrentQuestion);
        Assert.Equal(failed, session.Draft);
        Assert.Equal(previous, Assert.Single(session.Questions).Question);

        var shown = Assert.IsType<AssessmentResult.Assessed>(session.Current);
        Assert.Equal(previousResult.Summary, shown.Summary);
        Assert.Equal(ProposalStatus.Unavailable, shown.Proposal!.Status);
        Assert.Equal(previousResult.Proposal!.ProposalId, shown.Proposal.ProposalId);
        Assert.False(session.IsActionable(shown.Proposal));
        Assert.Equal(0, _approvals.Approvals);
    }

    [Fact]
    public async Task Absolute_limit_during_a_run_publishes_nothing()
    {
        await using var session = Session();
        _investigation.Hold = true;
        var run = session.StartAsync("Why was M-1042 not paid?");

        // The registry ends the session at the absolute limit while the run is still going.
        _user.IsAuthenticated = false;
        _investigation.LastProgress!.Report(new InvestigationProgress.ToolStarted("get_payout", "Payout P-1"));
        _investigation.Complete(Results.WithPendingTicket());
        await run;

        Assert.Empty(session.Steps);
        Assert.Empty(session.Questions);
        Assert.Null(session.Current);
        Assert.Equal(NotAssessedReason.NotAuthorized, session.Notice!.Reason);
        Assert.False(session.Busy);
    }

    [Fact]
    public async Task A_run_that_throws_after_the_session_ended_is_shown_as_not_authorized()
    {
        await using var session = Session();
        _investigation.Hold = true;
        var run = session.StartAsync("Why was M-1042 not paid?");

        _user.IsAuthenticated = false;
        _investigation.Fail(new InvalidOperationException("boom"));
        await run;

        Assert.Equal(NotAssessedReason.NotAuthorized, session.Notice!.Reason);
        Assert.Empty(session.Questions);
    }

    [Fact]
    public async Task A_live_session_publishes_as_before()
    {
        await using var session = Session();
        _investigation.Hold = true;
        var run = session.StartAsync("Why was M-1042 not paid?");

        _investigation.LastProgress!.Report(new InvestigationProgress.ToolStarted("get_payout", "Payout P-1"));
        _investigation.Complete(Results.WithPendingTicket());
        await run;

        Assert.Single(session.Steps);
        Assert.Single(session.Questions);
        Assert.Null(session.Notice);
        Assert.True(session.IsActionable(Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!));
    }

    [Fact]
    public async Task A_failure_before_the_run_suspends_is_shown_as_unavailable()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");
        var previous = Assert.IsType<AssessmentResult.Assessed>(session.Current);

        _investigation.Throw = new InvalidOperationException("boom");
        await session.StartAsync("Why is M-2001 held?");

        AssertFailedOver(session, "Why is M-2001 held?", "Why was M-1042 not paid?", previous);
    }

    // The session never decides for the server: a failure, wherever it happened, makes no approval or reset call.
    [Fact]
    public async Task A_failure_changes_nothing_on_the_server()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");

        _investigation.Throw = new InvalidOperationException("boom");
        await session.StartAsync("Why is M-2001 held?");
        await session.ApproveAsync();

        Assert.Equal(0, _approvals.Approvals);
        Assert.Equal(0, _approvals.Rejections);
        Assert.Equal(0, _investigation.StartNewCalls);
    }

    [Fact]
    public async Task A_failure_after_the_run_suspends_is_shown_as_unavailable()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");
        var previous = Assert.IsType<AssessmentResult.Assessed>(session.Current);

        _investigation.Hold = true;
        var run = session.StartAsync("Why is M-2001 held?");
        Assert.True(session.Busy);
        _investigation.Fail(new InvalidOperationException("boom"));
        await run;

        AssertFailedOver(session, "Why is M-2001 held?", "Why was M-1042 not paid?", previous);
    }

    [Fact]
    public async Task A_cancel_that_throws_is_shown_as_cancelled()
    {
        await using var session = Session();
        _investigation.Hold = true;
        _investigation.CancelThrows = true;

        var run = session.StartAsync("Why was M-1042 not paid?");
        session.Cancel();
        await run;

        Assert.False(session.Busy);
        Assert.Equal(NotAssessedReason.Cancelled, session.Notice?.Reason);
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task A_failure_is_logged_by_type_without_the_question_or_message()
    {
        await using var session = Session();
        _investigation.Throw = new InvalidOperationException("secret detail");

        await session.StartAsync("Why was M-1042 not paid?");

        var entry = Assert.Single(_log.Entries);
        Assert.Contains("InvalidOperationException", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("M-1042", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_runs_again_after_a_failure_without_retrying_on_its_own()
    {
        await using var session = Session();
        _investigation.Throw = new InvalidOperationException("boom");
        await session.StartAsync("Why was M-1042 not paid?");
        Assert.Single(_investigation.Asked);

        _investigation.Throw = null;
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");

        Assert.Equal(2, _investigation.Asked.Count);
        Assert.Null(session.Notice);
        Assert.IsType<AssessmentResult.Assessed>(session.Current);
    }

    [Fact]
    public async Task A_failed_decision_leaves_the_proposal_pending_and_can_be_tried_again()
    {
        await using var session = Session();
        _investigation.Answer = _ => Results.WithPendingTicket();
        await session.StartAsync("Why was M-1042 not paid?");

        _approvals.Throw = new TaskCanceledException("store timed out");
        await session.ApproveAsync();

        Assert.False(session.Deciding);
        Assert.True(session.DecisionFailed);
        var pending = Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!;
        Assert.True(session.IsActionable(pending));
        Assert.Contains("TaskCanceledException", Assert.Single(_log.Entries).Text, StringComparison.Ordinal);

        _approvals.Throw = null;
        await session.ApproveAsync();

        Assert.False(session.DecisionFailed);
        Assert.Equal(ProposalStatus.Approved, Assert.IsType<AssessmentResult.Assessed>(session.Current).Proposal!.Status);
        Assert.Equal(2, _approvals.Approvals);
    }

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
        await using var session = new InvestigationSession(_investigation, approvals, _user, _time, NullLogger<InvestigationSession>.Instance);
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
