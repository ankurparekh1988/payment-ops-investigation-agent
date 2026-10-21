using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Investigating;

internal static class InvestigationPipeline
{
    /// <summary>
    /// The framework's tool loop, fail-closed: one call at a time, no exception text to the model, an exception
    /// or an unknown tool ends the loop rather than being retried, and its iteration cap sits above ours.
    /// </summary>
    public static IChatClient FunctionInvoking(IChatClient model, InvestigationLimits limits) =>
        model.AsBuilder().UseFunctionInvocation(configure: functions =>
        {
            functions.AllowConcurrentInvocation = false;
            functions.IncludeDetailedErrors = false;
            functions.MaximumConsecutiveErrorsPerRequest = 0;
            functions.TerminateOnUnknownCalls = true;
            functions.MaximumIterationsPerRequest = limits.MaxToolCalls + 2;
        }).Build();
}
