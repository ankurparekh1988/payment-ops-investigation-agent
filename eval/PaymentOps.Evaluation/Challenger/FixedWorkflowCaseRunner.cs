using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Agent;
using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Evaluation.Running;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Challenger;

/// <summary>
/// The fixed workflow in the same services as the agent. Its validated result crosses the same boundary the
/// investigation service applies: an Engineer's proposal is kept in the app's proposal store, so approval and
/// ticket creation go through the same path; a Reader's stays a draft.
/// </summary>
internal sealed class FixedWorkflowCaseRunner(
    AiRelease release,
    InvestigationEndpoints endpoints,
    TokenCredential credential,
    Func<IChatClient, IChatClient>? substituteModel = null,
    Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge = null)
    : CaseRunner(release, endpoints, credential, substituteModel, substituteKnowledge)
{
    private static readonly IReadOnlyList<KnownProcessor> Processors = KnownProcessor.FromDemoData();

    protected override async Task<AssessmentResult> InvestigateAsync(IServiceProvider services, string question, IProgress<InvestigationProgress> progress, CancellationToken cancellationToken)
    {
        var user = services.GetRequiredService<IUserContext>();
        var workflow = new FixedWorkflow(
            services.GetRequiredService<IChatClient>(),
            services.GetServices<IInvestigationTool>(),
            services.GetRequiredService<IInvestigationClock>(),
            user,
            services.GetRequiredService<IOptions<InvestigationLimits>>(),
            services.GetRequiredService<IOptions<ToolOptions>>(),
            services.GetRequiredService<TimeProvider>(),
            Processors);
        var result = await workflow.InvestigateAsync(question, progress, cancellationToken) with { InvestigationId = Guid.NewGuid() };

        // As the investigation service does: only an Engineer can approve, so only an Engineer's proposal is kept.
        if (result is AssessmentResult.Assessed { Proposal: { } draft } assessed && user.IsInRole(OpsRoles.Engineer))
        {
            var kept = services.GetRequiredService<ProposalStore>().Open(draft, result.InvestigationId, Guid.NewGuid(), user.UserId);
            result = assessed with { Proposal = kept ?? draft with { Status = ProposalStatus.Unavailable } };
        }

        return result;
    }
}
