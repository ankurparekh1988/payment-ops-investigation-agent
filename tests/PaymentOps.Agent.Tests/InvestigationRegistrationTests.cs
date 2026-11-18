using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Security;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Tests;

public sealed class InvestigationRegistrationTests
{
    internal static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());
        services.AddSingleton(release);
        services.AddSingleton<IPromptProvider>(new PromptProvider(release));
        services.AddSingleton<TokenCredential>(new StaticTokenCredential());
        services.AddSingleton<IUserContext>(TestUser.General);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDemoData(new ConfigurationBuilder().Build());
        services.AddInvestigation(new InvestigationEndpoints(new Uri("https://search.example"), new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default"));
        return services;
    }

    [Fact]
    public void Prompt_Shields_screens_when_the_content_safety_endpoint_is_configured()
    {
        var services = Services();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentOps:ContentSafety:Endpoint"] = "https://cs.example/" }).Build());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HarmAwareScreen>(provider.GetRequiredService<IUntrustedTextScreen>());
    }

    [Fact]
    public void Without_a_content_safety_endpoint_the_screen_fails_closed()
    {
        using var provider = Services().BuildServiceProvider();

        Assert.IsType<UnavailableScreen>(provider.GetRequiredService<IUntrustedTextScreen>());
    }

    // The evaluation loads the release pointed at its own index; the trace has to name that one, not the live one.
    [Fact]
    public void The_trace_context_comes_from_the_loaded_release()
    {
        var services = Services();
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());
        services.AddSingleton(release with { KnowledgeIndex = release.EvaluationIndex });

        using var provider = services.BuildServiceProvider();

        Assert.Equal(release.EvaluationIndex, provider.GetRequiredService<TraceContext>().KnowledgeIndex);
    }

    [Fact]
    public void The_question_classifier_calls_the_screen_deployment()
    {
        using var provider = Services().BuildServiceProvider();
        var release = provider.GetRequiredService<AiRelease>();

        var classifier = provider.GetRequiredKeyedService<IChatClient>(HarmClassifier.PromptId);
        var investigation = provider.GetRequiredService<IChatClient>();

        Assert.Equal(release.ScreenDeployment, classifier.GetService<ChatClientMetadata>()?.DefaultModelId);
        Assert.Equal(release.ActiveChatDeployment, investigation.GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void Small_and_chat_clients_call_their_own_deployments()
    {
        var services = Services();
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource()) with { RoutingMode = RoutingMode.Cascade };
        services.AddSingleton(release);
        using var provider = services.BuildServiceProvider();

        var routing = provider.GetRequiredService<ModelRouting>();

        Assert.Equal(release.RoutingMode, routing.Mode);
        Assert.Equal(release.ActiveChatDeployment, routing.For(ModelTier.Chat).GetService<ChatClientMetadata>()?.DefaultModelId);
        Assert.Equal(release.SmallDeployment, routing.For(ModelTier.Small).GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void Off_starts_on_chat_and_cascade_on_the_small_model()
    {
        var chat = new ScriptedChatClient();
        var small = new ScriptedChatClient();

        Assert.Equal(ModelTier.Chat, new ModelRouting(RoutingMode.Off, chat, small).FirstTier);
        Assert.Equal(ModelTier.Small, new ModelRouting(RoutingMode.Cascade, chat, small).FirstTier);
    }

    [Fact]
    public void Off_builds_no_small_client()
    {
        using var provider = Services().BuildServiceProvider();

        Assert.Null(provider.GetRequiredService<ModelRouting>().Small);
    }

    [Fact]
    public void A_release_without_a_small_model_has_no_small_client()
    {
        var services = Services();
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());
        services.AddSingleton(release with { SmallDeployment = null });

        using var provider = services.BuildServiceProvider();
        var routing = provider.GetRequiredService<ModelRouting>();

        Assert.Null(routing.Small);
        Assert.Equal(ModelTier.Chat, routing.FirstTier);
    }

    [Fact]
    public void Cascade_without_a_small_client_is_refused() =>
        Assert.Throws<ArgumentException>(() => new ModelRouting(RoutingMode.Cascade, new ScriptedChatClient(), null));

    [Fact]
    public void The_investigation_is_gated_and_scoped()
    {
        var services = Services();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var service = first.ServiceProvider.GetRequiredService<IInvestigationService>();
        Assert.IsType<InFlightGate>(service);
        Assert.Same(service, first.ServiceProvider.GetRequiredService<IInvestigationService>());
        Assert.NotSame(service, second.ServiceProvider.GetRequiredService<IInvestigationService>());
    }

    /// <summary>Stands in for the host's usage guard: holds its first investigation open and counts what reaches it.</summary>
    private sealed class HoldingGuard : IInvestigationService
    {
        public static int Calls;

        public static TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConversationStatus Conversation => new(0, 0, false);

        public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Release.Task;
            return new AssessmentResult.NotAssessed(NotAssessedReason.Cancelled);
        }

        public void StartNew()
        {
        }

        public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // The host's guard sits inside the one-at-a-time gate, so a click while one runs never reaches it and counts nothing.
    [Fact]
    public async Task A_guard_sits_inside_the_in_flight_gate()
    {
        var services = Services();
        services.AddInvestigation(new InvestigationEndpoints(new Uri("https://search.example"), new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default"), guard: (_, _) => new HoldingGuard());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var investigations = scope.ServiceProvider.GetRequiredService<IInvestigationService>();

        var first = investigations.InvestigateAsync("q", null, CancellationToken.None);
        var second = await investigations.InvestigateAsync("q", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.Busy, Assert.IsType<AssessmentResult.NotAssessed>(second).Reason);
        Assert.Equal(1, HoldingGuard.Calls);
        HoldingGuard.Release.SetResult();
        await first;
    }

    [Fact]
    public void The_default_budget_leaves_room_for_history()
    {
        using var provider = Services().BuildServiceProvider();

        Assert.Equal(32_000, provider.GetRequiredService<IOptions<ConversationOptions>>().Value.InputBudgetTokens);
    }

    // 12 × 2,300 + 2,000 output + 1,000 question characters is under 32,000, but the fixed prompt then leaves no history.
    [Fact]
    public void A_budget_the_fixed_prompt_leaves_no_history_in_is_refused()
    {
        var services = Services();
        services.Configure<InvestigationLimits>(o => o.MaxToolCalls = 12);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ConversationOptions>>().Value);
    }

    [Fact]
    public void The_service_contract_has_no_grounded_answer() =>
        Assert.Null(typeof(IInvestigationService).GetMethod("AskAsync"));

    [Fact]
    public async Task The_registered_model_ends_a_turn_on_a_filtered_completion()
    {
        var services = Services();
        services.AddInvestigation(
            new InvestigationEndpoints(new Uri("https://search.example"), new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default"),
            _ => new ScriptedChatClient(_ => ModelFailureTests.Filtered()));
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<ContentFilteredException>(() => provider.GetRequiredService<IChatClient>().GetResponseAsync("q"));
    }

    // Startup and registration read the endpoint the same way, so one that can't be called can't pass startup.
    [Theory]
    [InlineData("https://cs.example/", true)]
    [InlineData("cs.example", false)]
    [InlineData("/contentsafety", false)]
    [InlineData("", false)]
    public void Only_an_absolute_https_endpoint_is_a_content_safety_endpoint(string value, bool usable)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ContentSafetyOptions.EndpointKey] = value }).Build();

        Assert.Equal(usable, ContentSafetyOptions.Endpoint(configuration) is not null);
    }
}
