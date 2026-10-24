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
/// Runs each case through the app's investigation service against the deployed model and index. Each case gets
/// its own services, so nothing (conversation, proposals, tickets) carries from one case to the next.
/// </summary>
internal sealed class AgentCaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel = null,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge = null) : ICaseRunner
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
                result = await scope.ServiceProvider.GetRequiredService<IInvestigationService>().InvestigateAsync(c.Question, first, cancellationToken);
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
}
