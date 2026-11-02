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
/// to; a second unscreened ask counts against the system.
/// </summary>
internal abstract class CaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge,
    Action<IServiceCollection>? configure,
    TimeSpan reaskDelay) : ICaseRunner
{
    // Throttling is per minute, so asking again at once would mostly meet the same limit.
    protected static readonly TimeSpan DefaultReaskDelay = TimeSpan.FromSeconds(15);

    private static readonly string Unscreened = Outcomes.NotAssessed(NotAssessedReason.Unscreened);

    public async Task<RunOutcome> RunAsync(IReadOnlyList<InvestigationCase> cases, CancellationToken cancellationToken)
    {
        var results = new List<CaseResult>();
        var tickets = 0;
        foreach (var c in cases)
        {
            var (recorded, caseTickets) = await AskAsync(c, cancellationToken);
            if (recorded.Outcome == Unscreened && recorded.InfrastructureFailure is null)
            {
                await Task.Delay(reaskDelay, cancellationToken);
                (recorded, caseTickets) = await AskAsync(c, cancellationToken);
                recorded = recorded with { Reasked = true };
            }

            results.Add(recorded);
            tickets += caseTickets;

            // The run will be discarded, so the rest of it would only spend tokens.
            if (recorded.InfrastructureFailure is not null)
            {
                break;
            }
        }

        return new RunOutcome(results, tickets);
    }

    /// <summary>One question, with an Engineer's proposal already kept by the app's proposal store.</summary>
    protected abstract Task<AssessmentResult> InvestigateAsync(IServiceProvider services, string question, IProgress<InvestigationProgress> progress, CancellationToken cancellationToken);

    // Fresh services for every ask, so nothing from an earlier ask carries into this one.
    private async Task<(CaseResult Result, int Tickets)> AskAsync(InvestigationCase c, CancellationToken cancellationToken)
    {
        var recorder = new CaseRecorder();
        using var provider = EvaluationServices.Build(release, endpoints, credential, c, recorder, substituteModel, substituteKnowledge, configure);
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
        return (recorder.ToResult(c.Id, result, proposal, latency, first.FirstMs), recorder.Tickets);
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
    TimeSpan? reaskDelay = null)
    : CaseRunner(release, endpoints, credential, substituteModel, substituteKnowledge, configure, reaskDelay ?? DefaultReaskDelay)
{
    protected override Task<AssessmentResult> InvestigateAsync(IServiceProvider services, string question, IProgress<InvestigationProgress> progress, CancellationToken cancellationToken) =>
        services.GetRequiredService<IInvestigationService>().InvestigateAsync(question, progress, cancellationToken);
}
