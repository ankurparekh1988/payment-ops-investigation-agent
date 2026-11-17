using PaymentOps.Agent.Contracts;

namespace PaymentOps.Web.SmokeTests.Investigating;

/// <summary>
/// An investigation service the test drives: it answers at once, or holds each run until the test completes it or the
/// run is cancelled, which answers <see cref="NotAssessedReason.Cancelled"/> as the real service does.
/// </summary>
internal sealed class ScriptedInvestigationService : IInvestigationService
{
    private TaskCompletionSource<AssessmentResult>? _held;

    // A result from a run that reached the investigation service carries an id; a refusal before it doesn't.
    public Func<string, AssessmentResult> Answer { get; set; } = _ => new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable) { InvestigationId = Guid.NewGuid() };

    public bool Hold { get; set; }

    /// <summary>Thrown before the run suspends, as a failure inside the call itself would be.</summary>
    public Exception? Throw { get; set; }

    /// <summary>A held run that's cancelled throws this instead of answering Cancelled.</summary>
    public bool CancelThrows { get; set; }

    public List<string> Asked { get; } = [];

    public IProgress<InvestigationProgress>? LastProgress { get; private set; }

    public CancellationToken LastToken { get; private set; }

    public int StartNewCalls { get; private set; }

    public int ConversationReads { get; private set; }

    public ConversationStatus Conversation
    {
        get
        {
            ConversationReads++;
            return new(Asked.Count, Asked.Count, false);
        }
    }

    public Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        Asked.Add(question);
        LastProgress = progress;
        LastToken = cancellationToken;
        if (Throw is { } failure)
        {
            throw failure;
        }

        if (!Hold)
        {
            return Task.FromResult(Answer(question));
        }

        _held = new TaskCompletionSource<AssessmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = _held;
        var cancelThrows = CancelThrows;
        cancellationToken.Register(() =>
        {
            if (cancelThrows)
            {
                held.TrySetCanceled(cancellationToken);
            }
            else
            {
                held.TrySetResult(new AssessmentResult.NotAssessed(NotAssessedReason.Cancelled) { InvestigationId = Guid.NewGuid() });
            }
        });
        return held.Task;
    }

    public void Complete(AssessmentResult result) => _held!.TrySetResult(result);

    /// <summary>Fails the held run after it suspended.</summary>
    public void Fail(Exception failure) => _held!.TrySetException(failure);

    public void StartNew()
    {
        StartNewCalls++;
        Asked.Clear();
    }

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) =>
        Task.FromResult<SourceResult>(new SourceResult.NotFound());
}

/// <summary>Answers every decision with a set result and counts the calls.</summary>
internal sealed class CountingApprovalService(ApprovalResult answer) : IApprovalService
{
    public int Approvals { get; private set; }

    public int Rejections { get; private set; }

    /// <summary>Thrown by the next decisions instead of answering.</summary>
    public Exception? Throw { get; set; }

    public Task<ApprovalResult> ApproveAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        Approvals++;
        return Throw is { } failure ? Task.FromException<ApprovalResult>(failure) : Task.FromResult(answer);
    }

    public Task<ApprovalResult> RejectAsync(Guid proposalId, RejectionReason reason, string? note, CancellationToken cancellationToken)
    {
        Rejections++;
        return Throw is { } failure ? Task.FromException<ApprovalResult>(failure) : Task.FromResult(answer);
    }
}

internal static class Results
{
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    public static AssessmentResult.Assessed WithPendingTicket(string summary = "Fabrikam Pay's settlement file for 9 November is missing.") =>
        new(AssessmentOutcome.Assessed, new Statement(summary, [], []), [], null, Confidence.High, [], [])
        {
            InvestigationId = Guid.NewGuid(),
            Proposal = new TicketProposal(
                Guid.NewGuid(), ProposalStatus.Pending, "fabrikam-pay", ["M-1042"], new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9),
                "Fabrikam Pay settlement file missing", "No file arrived for the 9 November batch.",
                [], [new Citation("S1", "payout-delay--abc", "Payout delay", "Payout delay > The batch didn't close")],
                "FAB-7731", Now.AddMinutes(15), null),
        };
}
