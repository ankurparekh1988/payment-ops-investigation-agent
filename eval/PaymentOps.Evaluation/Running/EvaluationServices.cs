using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentOps.Agent;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Security;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Running;

/// <summary>
/// The app's own registration for one case, observed by a <see cref="CaseRecorder"/>: the model client, the
/// knowledge lookup, each tool and the ticket store are wrapped, never replaced, so the case runs as the app would.
/// </summary>
internal static class EvaluationServices
{
    /// <param name="substituteModel">Tests only: replaces the Foundry client with a scripted one.</param>
    /// <param name="substituteKnowledge">Tests only: replaces the release's Search lookup.</param>
    public static ServiceProvider Build(
        AiRelease release,
        InvestigationEndpoints endpoints,
        TokenCredential credential,
        InvestigationCase c,
        CaseRecorder recorder,
        Func<IChatClient, IChatClient>? substituteModel,
        Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge)
    {
        var services = new ServiceCollection();
        services.AddSingleton(release);
        services.AddSingleton<IPromptProvider>(new PromptProvider(release));
        services.AddSingleton(credential);
        services.AddSingleton<IUserContext>(new EvaluationUser(c.Access == KnowledgeClassification.Restricted, c.Role == InvestigationCase.Engineer ? OpsRoles.Engineer : OpsRoles.Reader));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDemoData(new ConfigurationBuilder().Build());
        services.AddInvestigation(endpoints, model => recorder.Observe(substituteModel?.Invoke(model) ?? model));

        // The last registration wins, so these wrap the app's own implementations in place.
        services.AddSingleton(sp => recorder.Observe(substituteKnowledge?.Invoke(sp) ?? ActivatorUtilities.CreateInstance<ReleaseKnowledgeLookup>(sp)));
        Wrap<IInvestigationTool>(services, recorder.Observe);
        Wrap<IIncidentStore>(services, recorder.Observe);
        return services.BuildServiceProvider();
    }

    // Every registration of T, wrapped where it stands; they're all factory registrations in the app's extensions.
    private static void Wrap<T>(ServiceCollection services, Func<T, T> observe)
        where T : class
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            var create = descriptor.ImplementationFactory
                ?? throw new InvalidOperationException($"{typeof(T).Name} is registered without a factory; the harness can't wrap it.");
            services.Remove(descriptor);
            services.AddSingleton(sp => observe((T)create(sp)));
        }
    }

    /// <summary>
    /// What became of the result's proposal. A pending one is approved the way its owner would, through the app's
    /// approval service, so a ticket exists only where an Engineer's own stored proposal was approved.
    /// </summary>
    public static async Task<string> SettleProposalAsync(AssessmentResult result, IApprovalService approvals, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvals);

        if (result is not AssessmentResult.Assessed assessed)
        {
            return ProposalStates.None;
        }

        return assessed.Proposal switch
        {
            null => assessed.ProposalWithheld ? ProposalStates.Withheld : ProposalStates.None,
            { Status: ProposalStatus.Draft } => ProposalStates.Draft,
            { Status: ProposalStatus.Unavailable } => ProposalStates.Unavailable,
            { Status: ProposalStatus.Pending, ProposalId: { } id } => await approvals.ApproveAsync(id, cancellationToken) switch
            {
                ApprovalResult.Created => ProposalStates.Approved,
                ApprovalResult.CapacityReached => ProposalStates.CapacityReached,
                _ => ProposalStates.ApprovalFailed,
            },
            var other => ToolJson.Word(other.Status).Replace('_', '-'),
        };
    }

    /// <summary>A signed-in Ops Reader or Engineer, with or without Restricted knowledge access.</summary>
    private sealed record EvaluationUser(bool CanAccessRestrictedKnowledge, string Role) : IUserContext
    {
        public bool IsAuthenticated => true;

        public string UserId => "evaluation";

        public string DisplayName => "Evaluation";

        public IReadOnlySet<string> Roles { get; } = OpsRoles.Effective([Role]);

        public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
