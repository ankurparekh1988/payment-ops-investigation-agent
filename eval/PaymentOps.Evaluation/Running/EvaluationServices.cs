using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentOps.Agent;
using PaymentOps.Agent.Contracts;
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
    public const string ContentSafetyEndpointKey = "PaymentOps:ContentSafety:Endpoint";

    /// <summary>
    /// The app configuration a live evaluation supplies: the Content Safety endpoint, as the app reads it in Azure.
    /// Without one the configuration stays empty, as it is for tests.
    /// </summary>
    public static Action<IServiceCollection> AppConfiguration(Uri? contentSafetyEndpoint) => services =>
        services.Replace(ServiceDescriptor.Singleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ContentSafetyEndpointKey] = contentSafetyEndpoint?.ToString(),
        }).Build()));

    /// <param name="substituteModel">Tests only: replaces the Foundry client with a scripted one.</param>
    /// <param name="substituteKnowledge">Tests only: replaces the release's Search lookup.</param>
    /// <param name="configure">Adjusts the app's services: the configuration a live run supplies, or a test's options such as a shorter turn timeout.</param>
    public static ServiceProvider Build(
        AiRelease release,
        InvestigationEndpoints endpoints,
        TokenCredential credential,
        InvestigationCase c,
        CaseRecorder recorder,
        Func<IChatClient, IChatClient>? substituteModel,
        Func<IServiceProvider, IKnowledgeLookup>? substituteKnowledge,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(release);
        services.AddSingleton<IPromptProvider>(new PromptProvider(release));
        services.AddSingleton(credential);
        services.AddSingleton<IUserContext>(new EvaluationUser(c.Access == KnowledgeClassification.Restricted, c.Role == InvestigationCase.Engineer ? OpsRoles.Engineer : OpsRoles.Reader));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        // The app reads its settings from configuration, as it does in Azure; a live run replaces this through configure.
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDemoData(configuration);
        services.AddInvestigation(endpoints, model => recorder.Observe(substituteModel?.Invoke(model) ?? model));

        Wrap<IKnowledgeLookup>(services, (sp, lookup) => recorder.Observe(substituteKnowledge?.Invoke(sp) ?? lookup));
        Wrap<IInvestigationTool>(services, (_, tool) => recorder.Observe(tool));
        Wrap<IIncidentStore>(services, (_, store) => recorder.Observe(store));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // Every registration of T, wrapped where it stands with its own lifetime, however the app registered it.
    private static void Wrap<T>(ServiceCollection services, Func<IServiceProvider, T, T> observe)
        where T : class
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            Func<IServiceProvider, object> create = descriptor.ImplementationFactory
                ?? (descriptor.ImplementationInstance is { } instance ? _ => instance : sp => ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(typeof(T), sp => observe(sp, (T)create(sp)), descriptor.Lifetime));
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
            var other => Outcomes.Word(other.Status),
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
