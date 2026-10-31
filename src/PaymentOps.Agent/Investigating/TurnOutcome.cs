using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Model;

using NotAssessed = PaymentOps.Agent.Contracts.AssessmentResult.NotAssessed;

namespace PaymentOps.Agent.Investigating;

/// <summary>Why a turn's work ended in failure, for the caller's diagnostics.</summary>
internal enum TurnFailure
{
    /// <summary>The model or a service it depends on failed, or timed out on its own.</summary>
    ServiceFailed,

    /// <summary>A tool or the loop failed unexpectedly.</summary>
    Aborted,

    /// <summary>The model's content filter refused the prompt or withheld the completion.</summary>
    Blocked,
}

/// <summary>
/// How a turn ends, whoever plans it: the work runs under the caller's cancellation and the turn timeout, and
/// however it finishes is mapped to one result the same way. Only the planning inside the turn differs.
/// </summary>
internal static class TurnOutcome
{
    /// <param name="work">The turn's planning and submission; it returns a result only to end the turn early.</param>
    /// <param name="failed">Told about a failure before it becomes a result, for logging.</param>
    public static async Task<AssessmentResult> RunAsync(
        TurnState turn,
        InvestigationLimits limits,
        TimeProvider time,
        IProgress<InvestigationProgress>? progress,
        Func<CancellationToken, Task<AssessmentResult?>> work,
        Action<TurnFailure, Exception>? failed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(work);

        using var turnTimeout = new CancellationTokenSource(limits.TurnTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turnTimeout.Token);
        try
        {
            if (await work(linked.Token) is { } early)
            {
                return early;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NotAssessed(NotAssessedReason.Cancelled);
        }
        catch (OperationCanceledException) when (turnTimeout.IsCancellationRequested)
        {
            if (turn.Stop())
            {
                progress?.Report(new InvestigationProgress.LimitReached());
            }
        }
        catch (Exception ex) when (ex is ContentFilteredException || ModelFailure.IsContentFilterRejection(ModelFailure.LastAttempt(ex)))
        {
            // A policy decision, not an outage: retrying the same input would be refused again.
            failed?.Invoke(TurnFailure.Blocked, ex);
            return new NotAssessed(NotAssessedReason.Blocked);
        }
        catch (Exception ex) when (ex is OperationCanceledException || ModelFailure.IsServiceFailure(ex))
        {
            // A cancellation from neither the caller nor the turn timeout: the model or Search client timed out on its own.
            failed?.Invoke(TurnFailure.ServiceFailed, ex);
            return new NotAssessed(NotAssessedReason.ModelUnavailable);
        }
        catch (Exception ex)
        {
            // The framework ended the loop without retrying, and so do we.
            failed?.Invoke(TurnFailure.Aborted, ex);
            return new NotAssessed(NotAssessedReason.AssessmentUnverified);
        }

        return turn.Accepted is { } submission ? AssessmentPolicy.Apply(submission, turn)
            : new NotAssessed(turn.Stopped ? NotAssessedReason.LimitReached : NotAssessedReason.AssessmentUnverified);
    }
}
