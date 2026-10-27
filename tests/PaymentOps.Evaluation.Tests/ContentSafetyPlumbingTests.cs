using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Running;
using PaymentOps.Tests.Shared;
using PaymentOps.Tools;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

public sealed class ContentSafetyPlumbingTests
{
    [Fact]
    public void The_content_safety_endpoint_reaches_the_app_configuration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        EvaluationServices.AppConfiguration(new Uri("https://cs.invalid/"), "https://cognitiveservices.azure.com/.default")(services);

        var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        Assert.Equal("https://cs.invalid/", configuration[EvaluationServices.ContentSafetyEndpointKey]);
        Assert.Equal("https://cognitiveservices.azure.com/.default", configuration[EvaluationServices.FoundryTokenScopeKey]);
    }

    [Fact]
    public void The_app_always_gets_a_configuration_even_without_an_endpoint()
    {
        using var provider = EvaluationServices.Build(
            Release, Endpoints, new UnusedCredential(), Case(), new CaseRecorder(), null, null, EvaluationServices.AppConfiguration(null, Endpoints.FoundryTokenScope));

        var configuration = provider.GetRequiredService<IConfiguration>();
        Assert.Null(configuration[EvaluationServices.ContentSafetyEndpointKey]);
        Assert.Equal(Endpoints.FoundryTokenScope, configuration[EvaluationServices.FoundryTokenScopeKey]);
    }

    [Fact]
    public async Task The_unscreenable_copy_marks_an_infrastructure_failure()
    {
        var withheld = Section(Corpus.First(s => s.Classification == "general")) with { Content = InfrastructureFailures.UnscreenableCopy };
        var model = () => Model(
            ScriptedChatClient.Call(ToolNames.KnowledgeSearch, new { query = "missing settlement file" }),
            ScriptedChatClient.Call(ToolNames.SubmitAssessment, Submission()));

        var result = Assert.Single((await AgentRunner(model, new FixedKnowledge(withheld)).RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Equal(InfrastructureFailures.ScreeningUnavailable, result.InfrastructureFailure);
        Assert.NotNull(RunPlan.Invalid(new RunOutcome([result], 0)));
    }
}
