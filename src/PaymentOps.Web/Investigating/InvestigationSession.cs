using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.Investigating;

/// <summary>A question asked in this tab and what came back.</summary>
public sealed record SessionTurn(string Question, AssessmentResult Result);

/// <summary>
/// What the Investigate page shows and the run behind it, scoped to the circuit so both survive leaving the page.
/// It decides nothing the server owns: the conversation stays in <see cref="IInvestigationService"/>, proposals in
/// the approval service, and a card shows only what the server answered.
/// </summary>
public sealed class InvestigationSession(IInvestigationService investigation, IApprovalService approvals, TimeProvider time) : IAsyncDisposable
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
                return [.. _turns.Select(turn => turn with { Result = AsShown(turn.Result) })];
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
    public NotAssessedReason? Notice { get; private set; }

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

    // Only a run that reached the investigation service has an id; the service superseded the last proposal then.
    private void Record(string question, string? previousQuestion, AssessmentResult result)
    {
        if (result is AssessmentResult.NotAssessed refused && refused.InvestigationId == Guid.Empty)
        {
            Notice = refused.Reason;
            Draft = question;
            CurrentQuestion = previousQuestion;
            return;
        }

        SupersedePending();
        _turns.Add(new SessionTurn(question, result));
    }

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
        lock (_lock)
        {
            if (Deciding || Busy || _turns.Count == 0)
            {
                return;
            }

            Notice = null;

            turn = _turns[^1];
            if (turn.Result is not AssessmentResult.Assessed { Proposal: { ProposalId: not null } proposal } || !IsActionable(proposal))
            {
                return;
            }

            Deciding = true;
            DecisionFailed = false;
        }

        Changed?.Invoke();
        try
        {
            var assessed = (AssessmentResult.Assessed)turn.Result;
            var proposal = assessed.Proposal!;
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
        finally
        {
            Deciding = false;
            Changed?.Invoke();
        }
    }

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
