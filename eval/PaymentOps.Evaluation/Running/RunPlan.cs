using Azure;
using Azure.Identity;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Evaluation.Running;

/// <summary>Runs every case once, in order, against one system under evaluation.</summary>
internal interface ICaseRunner
{
    Task<RunOutcome> RunAsync(IReadOnlyList<InvestigationCase> cases, CancellationToken cancellationToken);
}

/// <summary>The evaluation environment couldn't give a fair run, even after the one repeat.</summary>
internal sealed class CouldNotEvaluateException(string message) : Exception(message);

/// <summary>Why the environment, rather than the system under evaluation, failed a case.</summary>
internal static class InfrastructureFailures
{
    public const string ModelUnavailable = "model-unavailable";
    public const string ModelBusy = "busy";
    public const string SearchUnavailable = "search-unavailable";
    public const string AuthenticationFailed = "authentication-failed";
    public const string EndpointUnavailable = "endpoint-unavailable";

    /// <summary>The failure an exception reaching the harness stands for, or null when it's a bug, not the environment.</summary>
    public static string? Of(Exception exception) => exception switch
    {
        AuthenticationFailedException => AuthenticationFailed,
        KnowledgeUnavailableException or RequestFailedException => SearchUnavailable,
        HttpRequestException => EndpointUnavailable,
        _ => null,
    };
}

/// <summary>
/// N runs of every case. A run the environment spoiled is discarded whole and repeated once; a second spoiled
/// attempt means the evaluation can't be made, never that it passes on fewer runs.
/// </summary>
internal static class RunPlan
{
    public static async Task<IReadOnlyList<RunOutcome>> RunAsync(ICaseRunner runner, IReadOnlyList<InvestigationCase> cases, int runs, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var outcomes = new List<RunOutcome>();
        for (var i = 1; i <= runs; i++)
        {
            var outcome = await runner.RunAsync(cases, cancellationToken);
            if (Invalid(outcome) is { } first)
            {
                Console.Error.WriteLine($"Run {i} was invalid ({first}); repeating it once.");

                // Throttling is per minute, so an immediate repeat would mostly meet the same limit.
                await Task.Delay(retryDelay, cancellationToken);
                outcome = await runner.RunAsync(cases, cancellationToken);
                if (Invalid(outcome) is { } second)
                {
                    throw new CouldNotEvaluateException($"Run {i} was invalid twice: {first}; then {second}. The evaluation environment is unavailable.");
                }
            }

            outcomes.Add(outcome);
        }

        return outcomes;
    }

    /// <summary>The first case the environment failed, or null when the run is valid.</summary>
    public static string? Invalid(RunOutcome outcome) =>
        outcome.Results.FirstOrDefault(r => r.InfrastructureFailure is not null) is { } r ? $"{r.CaseId}: {r.InfrastructureFailure}" : null;
}
