using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent;

/// <summary>Where the investigation's dependencies live.</summary>
/// <param name="FoundryTokenScope">Entra scope for Azure AI services; differs in sovereign clouds.</param>
public sealed record InvestigationEndpoints(Uri SearchEndpoint, Uri FoundryEndpoint, string FoundryTokenScope);

public static class InvestigationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the investigation behind <see cref="IInvestigationService"/>. Needs <see cref="AiRelease"/>,
    /// <see cref="IPromptProvider"/>, <see cref="TokenCredential"/> and a scoped <see cref="Domain.Security.IUserContext"/>.
    /// </summary>
    public static IServiceCollection AddInvestigation(this IServiceCollection services, InvestigationEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        services.AddSingleton(sp => KnowledgeRetriever.Create(endpoints.SearchEndpoint, sp.GetRequiredService<AiRelease>().KnowledgeIndex, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton(sp => KnowledgeSectionReader.Create(endpoints.SearchEndpoint, sp.GetRequiredService<AiRelease>().KnowledgeIndex, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IKnowledgeLookup, ReleaseKnowledgeLookup>();
        services.AddSingleton(sp => ModelClient.Create(endpoints.FoundryEndpoint, endpoints.FoundryTokenScope, sp.GetRequiredService<TokenCredential>(), sp.GetRequiredService<AiRelease>().ActiveChatDeployment));
        services.AddScoped<GroundedAnswerService>();
        services.AddScoped<IInvestigationService>(sp => new InFlightAskGate(sp.GetRequiredService<GroundedAnswerService>()));
        return services;
    }
}
