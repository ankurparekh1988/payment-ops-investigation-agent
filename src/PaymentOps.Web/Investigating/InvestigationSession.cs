using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Investigating;

/// <summary>A question asked in this tab and what came back.</summary>
public sealed record SessionTurn(string Question, AssessmentResult Result);

/// <summary>A question refused before its run started, and why.</summary>
public sealed record SessionNotice(NotAssessedReason Reason, string Question);

/// <summary>
/// What the Investigate page shows and the run behind it, scoped to the circuit so both survive leaving the page.
/// It decides nothing the server owns: the conversation stays in <see cref="IInvestigationService"/>, proposals in
/// the approval service, and a card shows only what the server answered.
/// </summary>
public sealed partial class InvestigationSession(
    IInvestigationService investigation, IApprovalService approvals, TimeProvider time, ILogger<InvestigationSession> logger) : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly List<InvestigationProgress> _steps = [];
    private readonly List<SessionTurn> _turns = [];
    private CancellationTokenSource? _run;

    // Read when it can change, not on every render: each read projects the conversation's history.
    private ConversationStatus _conversation = investigation.Conversation;

    // Bumped by every start, reset and disposal, so progress from an earlier run is recognised and dropped.
    private int _sequence;

    /// <summary>Raised from whichever thread changed the state; subscribers re-render through their own dispatcher.</summary>
    public event Action? Changed;

    /// <summary>The question being typed, kept across pages; Home fills it without running it.</summary>
    public string Draft { get; set; } = string.Empty;

    public IReadOnlyList<SessionTurn> Questions
    {
        get
        {
            lock (_lock)
            {
                // Only the latest turn can hold a pending proposal: a new run supersedes the one before.
                return _turns.Count == 0 ? [] : [.. _turns.SkipLast(1), _turns[^1] with { Result = AsShown(_turns[^1].Result) }];
            }
        }
    }

    public IReadOnlyList<InvestigationProgress> Steps
    {
        get
        {
            lock (_lock)
            {
                return [.. _steps];
            }
        }
    }

    /// <summary>The question of the run in progress, or of the last result.</summary>
    public string? CurrentQuestion { get; private set; }

    /// <summary>The latest result, with a pending proposal that has since expired shown as expired.</summary>
    public AssessmentResult? Current
    {
        get
        {
            lock (_lock)
            {
                return _turns.Count == 0 || Busy ? null : AsShown(_turns[^1].Result);
            }
        }
    }

    public ConversationStatus Conversation => _conversation;

    /// <summary>
    /// A question refused before its run started (paused, over a limit, busy, cancelled while admitted): the server kept
    /// no turn and superseded nothing, so the previous result and its card stay as they were, under this notice.
    /// </summary>
    public SessionNotice? Notice { get; private set; }

    public bool Busy { get; private set; }

    public bool Deciding { get; private set; }

    /// <summary>The last decision couldn't be stored; the proposal is still pending.</summary>
    public bool DecisionFailed { get; private set; }

    /// <summary>Counts finished runs, so a page can tell a new result from one it has already shown.</summary>
    public int Completed { get; private set; }

    public bool CanStart(string question) =>
        !Busy && !Deciding && !string.IsNullOrWhiteSpace(question) && !_conversation.Ended;

    /// <summary>Runs one question. The service applies its own timeouts; the session adds only Cancel.</summary>
    public async Task StartAsync(string question)
    {
        // Re-checked here: a disabled button is only the visible half.
        if (!CanStart(question))
        {
            return;
        }

        CancellationTokenSource run;
        int sequence;
        string? previousQuestion;
        lock (_lock)
        {
            previousQuestion = CurrentQuestion;
            _steps.Clear();
            sequence = ++_sequence;
            run = _run = new CancellationTokenSource();
            Busy = true;
            DecisionFailed = false;
            Notice = null;
            CurrentQuestion = question;
            Draft = string.Empty;
        }

        Changed?.Invoke();
        try
        {
            var result = await investigation.InvestigateAsync(question, new SessionProgress(this, sequence), run.Token);
            lock (_lock)
            {
                if (sequence == _sequence)
                {
                    Record(question, previousQuestion, result);
                    Completed++;
                }
            }
        }
        catch (Exception ex)
        {
            // Shown like a refusal before the run: the service kept no turn, and the earlier result stays under its own
            // question. Nothing is retried; Try again is the person's call.
            var cancelled = ex is OperationCanceledException && run.IsCancellationRequested;
            if (!cancelled)
            {
                Failed(logger, "investigation", ex.GetType().Name);
            }

            lock (_lock)
            {
                if (sequence == _sequence)
                {
                    Refuse(new SessionNotice(cancelled ? NotAssessedReason.Cancelled : NotAssessedReason.ModelUnavailable, question), previousQuestion);
                    Completed++;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_run, run))
                {
                    _run = null;
                }

                if (sequence == _sequence)
                {
                    Busy = false;
                }
            }

            run.Dispose();
            _conversation = investigation.Conversation;
            Changed?.Invoke();
        }
    }

    private void Record(string question, string? previousQuestion, AssessmentResult result)
    {
        if (result is AssessmentResult.NotAssessed refused && !ReachedTheService(refused))
        {
            Refuse(new SessionNotice(refused.Reason, question), previousQuestion);
            return;
        }

        SupersedePending();
        _turns.Add(new SessionTurn(question, result));
    }

    private void Refuse(SessionNotice notice, string? previousQuestion)
    {
        Notice = notice;
        Draft = notice.Question;
        CurrentQuestion = previousQuestion;
    }

    // Every run the investigation service starts carries its id, and the service supersedes the last proposal then.
    // A refusal before that (the usage guard, the one-at-a-time gate) has none, so nothing was superseded or kept.
    private static bool ReachedTheService(AssessmentResult result) => result.InvestigationId != Guid.Empty;

    public void Cancel()
    {
        lock (_lock)
        {
            _run?.Cancel();
        }
    }

    public Task ApproveAsync() => DecideAsync(id => approvals.ApproveAsync(id, CancellationToken.None), null);

    public Task RejectAsync(RejectionReason reason, string? note) =>
        DecideAsync(id => approvals.RejectAsync(id, reason, note, CancellationToken.None), reason);

    /// <summary>Whether the card may offer Approve and Reject: the stored proposal is pending and unexpired now.</summary>
    public bool IsActionable(TicketProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return proposal.Status == ProposalStatus.Pending && proposal.ExpiresAt > time.GetUtcNow();
    }

    /// <summary>Starts a new conversation and clears the page. Ignored while running or deciding.</summary>
    public void StartNew()
    {
        lock (_lock)
        {
            if (Busy || Deciding)
            {
                return;
            }

            investigation.StartNew();
            _conversation = investigation.Conversation;
            Notice = null;
            _sequence++;
            _turns.Clear();
            _steps.Clear();
            Draft = string.Empty;
            CurrentQuestion = null;
            DecisionFailed = false;
        }

        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _sequence++;
            _run?.Cancel();
        }

        Changed = null;
        return ValueTask.CompletedTask;
    }

    // The card shows what the server answered, never what the click hoped for.
    private async Task DecideAsync(Func<Guid, Task<ApprovalResult>> decide, RejectionReason? reason)
    {
        SessionTurn turn;
        AssessmentResult.Assessed assessed;
        TicketProposal proposal;
        lock (_lock)
        {
            if (Deciding || Busy || _turns.Count == 0)
            {
                return;
            }

            Notice = null;

            turn = _turns[^1];
            if (turn.Result is not AssessmentResult.Assessed { Proposal: { ProposalId: not null } pending } shown || !IsActionable(pending))
            {
                return;
            }

            (assessed, proposal) = (shown, pending);

            Deciding = true;
            DecisionFailed = false;
        }

        Changed?.Invoke();
        try
        {
            var outcome = await decide(proposal.ProposalId!.Value);
            lock (_lock)
            {
                // The page moved on while the decision ran; the card it was made from is gone.
                if (_turns.Count == 0 || !ReferenceEquals(_turns[^1], turn))
                {
                    return;
                }

                // The store failed; the proposal is still pending, so the card stays as it is.
                DecisionFailed = outcome is ApprovalResult.Unavailable;
                _turns[^1] = turn with { Result = assessed with { Proposal = Decided(proposal, outcome, reason) } };
            }

            _conversation = investigation.Conversation;
        }
        catch (Exception ex)
        {
            // The approval service puts a failed claim back to pending, and a ticket is keyed by its proposal, so trying
            // again can't make a second one.
            Failed(logger, "decision", ex.GetType().Name);
            lock (_lock)
            {
                DecisionFailed = _turns.Count > 0 && ReferenceEquals(_turns[^1], turn);
            }
        }
        finally
        {
            Deciding = false;
            Changed?.Invoke();
        }
    }

    // The type only: a message can carry the question, tool output or a store's details.
    [LoggerMessage(EventId = 3401, Level = LogLevel.Error, Message = "Investigation page: the {Operation} failed unexpectedly ({ExceptionType})")]
    private static partial void Failed(ILogger logger, string operation, string exceptionType);

    private static TicketProposal Decided(TicketProposal proposal, ApprovalResult outcome, RejectionReason? reason) => outcome switch
    {
        ApprovalResult.Created created => proposal with { Status = ProposalStatus.Approved, TicketId = created.TicketId },
        ApprovalResult.Rejected => proposal with { Status = ProposalStatus.Rejected, Rejection = reason },
        ApprovalResult.AlreadyDecided decided => proposal with { Status = decided.TicketId is null ? ProposalStatus.Rejected : ProposalStatus.Approved, TicketId = decided.TicketId },
        ApprovalResult.Expired => proposal with { Status = ProposalStatus.Expired },
        ApprovalResult.Superseded => proposal with { Status = ProposalStatus.Superseded },
        ApprovalResult.CapacityReached => proposal with { Status = ProposalStatus.CapacityReached },
        ApprovalResult.Unavailable => proposal,
        _ => proposal with { Status = ProposalStatus.Unavailable },
    };

    private void SupersedePending()
    {
        if (_turns.Count > 0 && _turns[^1].Result is AssessmentResult.Assessed { Proposal.Status: ProposalStatus.Pending } assessed)
        {
            _turns[^1] = _turns[^1] with { Result = assessed with { Proposal = assessed.Proposal! with { Status = ProposalStatus.Superseded } } };
        }
    }

    // Expiry is checked when shown, so a card left open never offers a decision the server would refuse.
    private AssessmentResult AsShown(AssessmentResult result) =>
        result is AssessmentResult.Assessed { Proposal: { Status: ProposalStatus.Pending } proposal } assessed && !IsActionable(proposal)
            ? assessed with { Proposal = proposal with { Status = ProposalStatus.Expired } }
            : result;

    // Reports arrive on the investigation's thread; each lands in the session, never in a component.
    private sealed class SessionProgress(InvestigationSession session, int sequence) : IProgress<InvestigationProgress>
    {
        public void Report(InvestigationProgress value)
        {
            lock (session._lock)
            {
                if (sequence != session._sequence)
                {
                    return;
                }

                session._steps.Add(value);
            }

            session.Changed?.Invoke();
        }
    }
}
