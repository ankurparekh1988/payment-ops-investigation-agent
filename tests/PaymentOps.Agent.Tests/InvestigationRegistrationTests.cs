using Azure.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Security;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Tests;

public sealed class InvestigationRegistrationTests
{
    [Fact]
    public void The_investigation_is_gated_and_scoped()
    {
        var services = new ServiceCollection();
        var release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());
        services.AddSingleton(release);
        services.AddSingleton<IPromptProvider>(new PromptProvider(release));
        services.AddSingleton<TokenCredential>(new StaticTokenCredential());
        services.AddSingleton<IUserContext>(TestUser.General);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<AnsweringOptions>();
        services.AddDemoData(new ConfigurationBuilder().Build());
        services.AddInvestigation(new InvestigationEndpoints(new Uri("https://search.example"), new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var service = first.ServiceProvider.GetRequiredService<IInvestigationService>();
        Assert.IsType<InFlightAskGate>(service);
        Assert.Same(service, first.ServiceProvider.GetRequiredService<IInvestigationService>());
        Assert.NotSame(service, second.ServiceProvider.GetRequiredService<IInvestigationService>());
    }
}
