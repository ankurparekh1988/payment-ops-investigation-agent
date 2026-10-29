using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent;

/// <summary>Where the investigation's dependencies live.</summary>
/// <param name="FoundryTokenScope">Entra scope for Azure AI services; differs in sovereign clouds.</param>
public sealed record InvestigationEndpoints(Uri SearchEndpoint, Uri FoundryEndpoint, string FoundryTokenScope);

public static class InvestigationServiceCollectionExtensions
{
    // Shared for the process; each call's own deadline comes from the guard's screening budget.
    private static readonly HttpClient ScreenHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Registers the investigation behind <see cref="IInvestigationService"/> and ticket decisions behind <see cref="IApprovalService"/>.
    /// Needs <see cref="AiRelease"/>, <see cref="IPromptProvider"/>, <see cref="TokenCredential"/>, a scoped
    /// <see cref="Domain.Security.IUserContext"/>, the data ports with an <see cref="Domain.Investigation.IInvestigationClock"/>
    /// for the tools, and an <see cref="Domain.Incidents.IIncidentStore"/>.
    /// </summary>
    /// <param name="observeModel">Wraps the model client, for an evaluation run that records every request.</param>
    public static IServiceCollection AddInvestigation(this IServiceCollection services, InvestigationEndpoints endpoints, Func<IChatClient, IChatClient>? observeModel = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        services.AddSingleton(sp => KnowledgeRetriever.Create(endpoints.SearchEndpoint, sp.GetRequiredService<AiRelease>().KnowledgeIndex, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton(sp => KnowledgeSectionReader.Create(endpoints.SearchEndpoint, sp.GetRequiredService<AiRelease>().KnowledgeIndex, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IKnowledgeLookup, ReleaseKnowledgeLookup>();
        services.AddSingleton(sp =>
        {
            var model = ModelClient.Create(endpoints.FoundryEndpoint, endpoints.FoundryTokenScope, sp.GetRequiredService<TokenCredential>(), sp.GetRequiredService<AiRelease>().ActiveChatDeployment);
            return ContentFilter.Checked(observeModel is null ? model : observeModel(model));
        });
        services.AddInvestigationTools();

        // Prompt Shields lives on the Foundry account's generic endpoint, configured separately from the model's. Without
        // one (only Development allows that) nothing can be checked, so untrusted tool text is withheld.
        services.AddSingleton<ScreenCache>();
        services.AddSingleton<IUntrustedTextScreen>(sp =>
            Uri.TryCreate(sp.GetService<IConfiguration>()?[$"{ContentSafetyOptions.SectionName}:Endpoint"], UriKind.Absolute, out var contentSafety)
                ? new PromptShieldsScreen(ScreenHttp, contentSafety, endpoints.FoundryTokenScope, sp.GetRequiredService<TokenCredential>(), sp.GetRequiredService<ScreenCache>())
                : new UnavailableScreen());
        services.AddOptions<InvestigationLimits>();
        services.AddSingleton<FixedPrompt>();

        // The current turn's reservations must leave room for history, or every conversation would end after one
        // question. Checked with the same calculation that budgets history at run time.
        services.AddOptions<ConversationOptions>()
            .Validate<IOptions<InvestigationLimits>, FixedPrompt>(
                (o, limits, prompt) => HistoryProjection.HistoryBudgetChars(o, limits.Value, prompt.Chars) > 0,
                $"{ConversationOptions.SectionName}:InputBudgetTokens must exceed the instructions, tool definitions, question, tool results and output each turn reserves.")
            .ValidateOnStart();
        services.AddOptions<ApprovalOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SourceService>();
        services.AddScoped<InvestigationAgentRunner>();
        services.AddScoped<Conversation>();

        // Proposals outlive a circuit only until they expire; the approval service is per circuit, like its user.
        services.AddSingleton<ProposalStore>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IInvestigationService>(sp => new InFlightGate(ActivatorUtilities.CreateInstance<InvestigationService>(sp)));
        return services;
    }
}
