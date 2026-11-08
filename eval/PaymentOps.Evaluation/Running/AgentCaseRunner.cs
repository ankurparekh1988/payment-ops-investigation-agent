using System.Diagnostics;

using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent;
using PaymentOps.Agent.Contracts;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Running;

/// <summary>
/// Runs each case against the deployed model and index in the app's own services, recorded from outside. Each
/// case gets its own services, so nothing (conversation, proposals, tickets) carries from one case to the next.
/// A system under evaluation differs only in how it investigates; proposals are settled the same way for both.
/// A question the screen couldn't check never reached the system, so it's asked once more, as the app tells the person
/// to. Still unscreened, the attempt measured the screen's availability rather than the system, so it's the
/// environment's; the run plan's case retry starts the whole case over.
/// </summary>
internal abstract class CaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge,
    Action<IServiceCollection>? configure,
    TimeSpan reaskDelay,
    Func<InvestigationCase, Action<IServiceCollection>?>? overlay = null) : ICaseRunner
{
    // Throttling is per minute, so asking again at once would mostly meet the same limit.
    protected static readonly TimeSpan DefaultReaskDelay = TimeSpan.FromSeconds(15);

    private static readonly string Unscreened = Outcomes.NotAssessed(NotAssessedReason.Unscreened);

    public async Task<CaseAttempt> RunCaseAsync(InvestigationCase c, CancellationToken cancellationToken)
    {
        var attempt = await AskAsync(c, cancellationToken);
        if (attempt.Result.Outcome == Unscreened && attempt.Result.InfrastructureFailure is null)
        {
            await Task.Delay(reaskDelay, cancellationToken);
            attempt = await AskAsync(c, cancellationToken);
            attempt = attempt with
            {
                Result = attempt.Result with
                {
                    Reasked = true,
                    InfrastructureFailure = attempt.Result.InfrastructureFailure
                        ?? (attempt.Result.Outcome == Unscreened ? InfrastructureFailures.ScreeningUnavailable : null),
                },
            };
        }

        return attempt;
    }

    /// <summary>One question, with an Engineer's proposal already kept by the app's proposal store.</summary>
    protected abstract Task<AssessmentResult> InvestigateAsync(IServiceProvider services, string question, IProgress<InvestigationProgress> progress, CancellationToken cancellationToken);

    // Fresh services for every ask, so nothing from an earlier ask carries into this one.
    private async Task<CaseAttempt> AskAsync(InvestigationCase c, CancellationToken cancellationToken)
    {
        var recorder = new CaseRecorder();
        using var provider = EvaluationServices.Build(release, endpoints, credential, c, recorder, substituteModel, substituteKnowledge, configure, overlay?.Invoke(c));
        using var scope = provider.CreateScope();

        var timer = Stopwatch.StartNew();
        var first = new FirstProgress(() => timer.ElapsedMilliseconds);
        AssessmentResult result;
        try
        {
            result = await InvestigateAsync(scope.ServiceProvider, c.Question, first, cancellationToken);
        }
        catch (Exception ex) when (InfrastructureFailures.Of(ex) is { } failure)
        {
            recorder.Fail(failure);
            result = new AssessmentResult.NotAssessed(NotAssessedReason.ModelUnavailable);
        }

        var latency = timer.ElapsedMilliseconds;
        var proposal = await EvaluationServices.SettleProposalAsync(result, scope.ServiceProvider.GetRequiredService<IApprovalService>(), cancellationToken);
        return new CaseAttempt(recorder.ToResult(c.Id, result, proposal, latency, first.FirstMs), recorder.Tickets);
    }
}

/// <summary>The investigation agent, through the app's investigation service.</summary>
internal sealed class AgentCaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel = null,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge = null,
    Action<IServiceCollection>? configure = null,
    TimeSpan? reaskDelay = null,
    Func<InvestigationCase, Action<IServiceCollection>?>? overlay = null)
    : CaseRunner(release, endpoints, credential, substituteModel, substituteKnowledge, configure, reaskDelay ?? DefaultReaskDelay, overlay)
{
    protected override Task<AssessmentResult> InvestigateAsync(IServiceProvider services, string question, IProgress<InvestigationProgress> progress, CancellationToken cancellationToken) =>
        services.GetRequiredService<IInvestigationService>().InvestigateAsync(question, progress, cancellationToken);
}
