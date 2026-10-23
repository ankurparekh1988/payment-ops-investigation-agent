using PaymentOps.Agent.Contracts;

namespace PaymentOps.Evaluation.Agent;

/// <summary>
/// Time to first progress: from submit to the first event the agent's own choice caused, a read tool starting or a
/// direct submission. Events the server raises on its own don't count. Reports arrive synchronously, in order.
/// </summary>
/// <param name="elapsedMs">Milliseconds since the question was submitted.</param>
internal sealed class FirstProgress(Func<long> elapsedMs) : IProgress<InvestigationProgress>
{
    public long? FirstMs { get; private set; }

    public void Report(InvestigationProgress value)
    {
        var at = elapsedMs();
        if (FirstMs is null && value is InvestigationProgress.ToolStarted or InvestigationProgress.Submitting)
        {
            FirstMs = at;
        }
    }
}
