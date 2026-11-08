using Azure;
using Azure.Identity;

using PaymentOps.Agent.Model;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Evaluation.Running;

/// <summary>Runs one case against one system under evaluation, in services of its own.</summary>
internal interface ICaseRunner
{
    Task<CaseAttempt> RunCaseAsync(InvestigationCase c, CancellationToken cancellationToken);
}

/// <summary>The evaluation environment couldn't give a fair result, even after the one retry.</summary>
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
    /// The copy that stands in for tool text Prompt Shields couldn't check. A request carrying it measured an outage
    /// rather than the agent. Matched as text until the harness can observe the screen directly.
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
/// N runs of every case. An attempt the environment spoiled is discarded and that case alone is run once more, so an
/// outage on one case costs one case. A second spoiled attempt, or more retries in a run than an isolated failure
/// explains, means the evaluation can't be made, never that it passes on fewer results.
/// </summary>
internal static class RunPlan
{
    /// <summary>Beyond this many in one run it's an outage, and the rest of the run wouldn't measure the system.</summary>
    public const int MaxCaseRetriesPerRun = 3;

    public static async Task<IReadOnlyList<RunOutcome>> RunAsync(ICaseRunner runner, IReadOnlyList<InvestigationCase> cases, int runs, TimeSpan retryDelay, EvaluationDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var outcomes = new List<RunOutcome>();
        for (var i = 1; i <= runs; i++)
        {
            outcomes.Add(await RunOnceAsync(runner, cases, i, retryDelay, diagnostics, cancellationToken));
        }

        return outcomes;
    }

    private static async Task<RunOutcome> RunOnceAsync(ICaseRunner runner, IReadOnlyList<InvestigationCase> cases, int run, TimeSpan retryDelay, EvaluationDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        var results = new List<CaseResult>();
        var tickets = 0;
        var retries = 0;
        foreach (var c in cases)
        {
            var mark = diagnostics.Mark();
            CaseAttempt attempt;
            try
            {
                attempt = await OnceMoreIfInvalidAsync(
                    $"Run {run}, case {c.Id}",
                    () => runner.RunCaseAsync(c, cancellationToken),
                    a => a.Result.InfrastructureFailure,
                    retryDelay,
                    cancellationToken,
                    reason =>
                    {
                        if (++retries > MaxCaseRetriesPerRun)
                        {
                            throw new CouldNotEvaluateException($"Run {run} needed more than {MaxCaseRetriesPerRun} case retries; case {c.Id} failed with {reason}. The evaluation environment is unavailable.");
                        }

                        diagnostics.CaseRetried(run, c.Id, reason, diagnostics.FailuresSince(mark));
                    });
            }
            catch (CouldNotEvaluateException ex) when (diagnostics.FailuresSince(mark) is { Count: > 0 } failures)
            {
                throw new CouldNotEvaluateException($"{ex.Message} During this case: {string.Join("; ", failures)}.");
            }

            results.Add(attempt.Result);
            tickets += attempt.Tickets;
        }

        return new RunOutcome(results, tickets);
    }

    /// <summary>
    /// The rule for any one attempt, a case or a retrieval run: an attempt the environment spoiled, by its result or by
    /// an exception it raised, is discarded and repeated once; a second spoiled attempt can't be evaluated.
    /// </summary>
    /// <param name="repeating">Told the reason before each repeat, and may refuse it by throwing.</param>
    public static async Task<T> OnceMoreIfInvalidAsync<T>(string name, Func<Task<T>> attempt, Func<T, string?> invalid, TimeSpan retryDelay, CancellationToken cancellationToken, Action<string>? repeating = null)
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
            repeating?.Invoke(reason);
            Console.Error.WriteLine($"{name} was invalid ({reason}); repeating it once.");

            // Throttling is per minute, so an immediate repeat would mostly meet the same limit.
            await Task.Delay(retryDelay, cancellationToken);
        }
    }
}
