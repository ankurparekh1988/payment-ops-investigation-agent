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
/// </summary>
internal abstract class CaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge) : ICaseRunner
{
    public async Task<RunOutcome> RunAsync(IReadOnlyList<InvestigationCase> cases, CancellationToken cancellationToken)
    {
        var results = new List<CaseResult>();
        var tickets = 0;
        foreach (var c in cases)
        {
            var recorder = new CaseRecorder();
            using var provider = EvaluationServices.Build(release, endpoints, credential, c, recorder, substituteModel, substituteKnowledge);
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
            var recorded = recorder.ToResult(c.Id, result, proposal, latency, first.FirstMs);
            results.Add(recorded);
            tickets += recorder.Tickets;

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
}

/// <summary>The investigation agent, through the app's investigation service.</summary>
internal sealed class AgentCaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel = null,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge = null)
    : CaseRunner(release, endpoints, credential, substituteModel, substituteKnowledge)
{
    protected override Task<AssessmentResult> InvestigateAsync(IServiceProvider services, string question, IProgress<InvestigationProgress> progress, CancellationToken cancellationToken) =>
        services.GetRequiredService<IInvestigationService>().InvestigateAsync(question, progress, cancellationToken);
}
