using Azure.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Safety;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Security;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Tests;

public sealed class InvestigationRegistrationTests
{
    private static ServiceCollection Services()
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

        Assert.IsType<PromptShieldsScreen>(provider.GetRequiredService<IUntrustedTextScreen>());
    }

    [Fact]
    public void Without_a_content_safety_endpoint_the_screen_fails_closed()
    {
        using var provider = Services().BuildServiceProvider();

        Assert.IsType<UnavailableScreen>(provider.GetRequiredService<IUntrustedTextScreen>());
    }

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
}
