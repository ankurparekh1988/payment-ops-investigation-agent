using Azure;
using Azure.Identity;

using PaymentOps.Agent.Model;
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
    public const string ScreeningUnavailable = "screening-unavailable";

    /// <summary>
    /// What the app sends the model in place of tool text Prompt Shields couldn't check. Seeing it means the screen
    /// was unavailable, so the case measured an outage rather than the agent. It must match the app's wording exactly.
    /// </summary>
    public const string UnscreenableCopy = "[withheld: this text couldn't be checked]";

    /// <summary>The failure an exception reaching the harness stands for, or null when it's a bug, not the environment.</summary>
    public static string? Of(Exception exception) => exception switch
    {
        AuthenticationFailedException => AuthenticationFailed,
        KnowledgeUnavailableException or RequestFailedException => SearchUnavailable,
        HttpRequestException => EndpointUnavailable,
        _ when ModelFailure.IsServiceFailure(exception) => ModelUnavailable,
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
            outcomes.Add(await OnceMoreIfInvalidAsync($"Run {i}", () => runner.RunAsync(cases, cancellationToken), Invalid, retryDelay, cancellationToken));
        }

        return outcomes;
    }

    /// <summary>
    /// The invalid-run rule for any one run: an attempt the environment spoiled, by its result or by an exception it
    /// raised, is discarded and repeated once; a second spoiled attempt can't be evaluated.
    /// </summary>
    public static async Task<T> OnceMoreIfInvalidAsync<T>(string name, Func<Task<T>> attempt, Func<T, string?> invalid, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(invalid);

        string? first = null;
        while (true)
        {
            string? reason;
            try
            {
                var result = await attempt();
                reason = invalid(result);
                if (reason is null)
                {
                    return result;
                }
            }
            catch (Exception ex) when (InfrastructureFailures.Of(ex) is { } failure)
            {
                reason = failure;
            }

            if (first is not null)
            {
                throw new CouldNotEvaluateException($"{name} was invalid twice: {first}; then {reason}. The evaluation environment is unavailable.");
            }

            first = reason;
            Console.Error.WriteLine($"{name} was invalid ({reason}); repeating it once.");

            // Throttling is per minute, so an immediate repeat would mostly meet the same limit.
            await Task.Delay(retryDelay, cancellationToken);
        }
    }

    /// <summary>The first case the environment failed, or null when the run is valid.</summary>
    public static string? Invalid(RunOutcome outcome) =>
        outcome.Results.FirstOrDefault(r => r.InfrastructureFailure is not null) is { } r ? $"{r.CaseId}: {r.InfrastructureFailure}" : null;
}
