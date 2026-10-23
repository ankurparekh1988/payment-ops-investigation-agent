using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Tests;

/// <summary>The real agent loop and tools over the shipped dataset; only the model and Search are scripted.</summary>
internal sealed class AgentFixture : IDisposable
{
    private static readonly AiRelease Release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());
    private readonly ServiceProvider _provider;

    /// <param name="time">The host's real clock, which the incident store stamps tickets with.</param>
    public AgentFixture(Dictionary<string, string?>? faults = null, IKnowledgeLookup? knowledge = null, int maxListItems = 20, TimeProvider? time = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(faults ?? []).Build();
        _provider = new ServiceCollection()
            .AddSingleton(time ?? TimeProvider.System)
            .AddDemoData(configuration, allowFaultInjection: faults is not null)
            .AddSingleton(knowledge ?? new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([])))
            .AddInvestigationTools(options =>
            {
                options.RetryBackoff = TimeSpan.Zero;
                options.MaxListItems = maxListItems;
            })
            .BuildServiceProvider();
    }

    public InvestigationAgentRunner Runner(IChatClient model, IUserContext? user = null, TimeProvider? time = null, ILogger<InvestigationAgentRunner>? logger = null) => new(
        model,
        _provider.GetServices<IInvestigationTool>(),
        FixedPrompt,
        _provider.GetRequiredService<IInvestigationClock>(),
        user ?? TestUser.Reader,
        Options.Create(new InvestigationLimits()),
        time ?? TimeProvider.System,
        logger ?? NullLogger<InvestigationAgentRunner>.Instance);

    public FixedPrompt FixedPrompt => new(_provider.GetServices<IInvestigationTool>(), new PromptProvider(Release), _provider.GetRequiredService<IInvestigationClock>());

    public IIncidentStore IncidentStore => _provider.GetRequiredService<IIncidentStore>();

    public IInvestigationTool Tool(string name) => _provider.GetServices<IInvestigationTool>().Single(t => t.Name == name);

    public void Dispose() => _provider.Dispose();

    /// <summary>A <c>submit_assessment</c> call's arguments.</summary>
    public static object Submission(
        string summaryEvidence = "merchant:M-1042",
        string[]? claimEvidence = null,
        string[]? claimCitations = null,
        string rootCause = "",
        string[]? rootEvidence = null,
        string[]? rootCitations = null,
        string outcome = "assessed",
        string confidence = "high",
        object? ticket = null) => new
        {
            outcome,
            summary = new { text = "M-1042's Fabrikam batch for 9 November didn't arrive.", evidence_ids = new[] { summaryEvidence }, citation_ids = Array.Empty<string>() },
            claims = new[] { new { text = "The merchant is routed to Fabrikam Pay.", evidence_ids = claimEvidence ?? ["merchant:M-1042"], citation_ids = claimCitations ?? [] } },
            root_cause = new { text = rootCause, evidence_ids = rootEvidence ?? [], citation_ids = rootCitations ?? [] },
            confidence,
            next_steps = Array.Empty<object>(),
            proposed_ticket = ticket,
        };
}

internal static class RunnerExtensions
{
    /// <summary>A first question: no history, nobody watching.</summary>
    public static Task<AssessmentResult> InvestigateAsync(this InvestigationAgentRunner runner, string question, CancellationToken cancellationToken) =>
        runner.InvestigateAsync(question, [], null, Guid.NewGuid(), cancellationToken);
}
