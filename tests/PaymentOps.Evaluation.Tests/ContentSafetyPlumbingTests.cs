using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Challenger;
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

    private static ScriptedChatClient SearchThenSubmit() => Model(
        ScriptedChatClient.Call(ToolNames.KnowledgeSearch, new { query = "missing settlement file" }),
        ScriptedChatClient.Call(ToolNames.SubmitAssessment, Submission()));

    [Fact]
    public async Task A_screen_outage_is_recorded_from_the_screen_itself()
    {
        var down = new ScriptedScreen(_ => ScreenVerdict.Unavailable);

        var result = Assert.Single((await AgentRunner(SearchThenSubmit, screen: down).RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Equal(InfrastructureFailures.ScreeningUnavailable, result.InfrastructureFailure);
    }

    // Tool text that happens to read like the withheld copy is data the screen passed, not an outage.
    [Fact]
    public async Task Text_like_the_withheld_copy_is_not_an_outage()
    {
        var lookalike = GeneralSection with { Content = "[withheld: this text couldn't be checked]" };

        var result = Assert.Single((await AgentRunner(SearchThenSubmit, new FixedKnowledge(lookalike)).RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Null(result.InfrastructureFailure);
    }

    [Fact]
    public async Task A_challenger_screen_outage_is_recorded_too()
    {
        var down = new ScriptedScreen(_ => ScreenVerdict.Unavailable);
        var challenger = new FixedWorkflowCaseRunner(Release, Endpoints, new UnusedCredential(), _ => Model(
            ScriptedChatClient.Text("""{"merchant_ids":["M-1042"],"processor_ids":[],"start_date":"2026-11-09","end_date":"2026-11-10"}"""),
            ScriptedChatClient.Call(ToolNames.SubmitAssessment, Submission())), _ => new FixedKnowledge(GeneralSection), Screening(down));

        var result = Assert.Single((await challenger.RunAsync([Case()], CancellationToken.None)).Results);

        Assert.Equal(InfrastructureFailures.ScreeningUnavailable, result.InfrastructureFailure);
    }
}
