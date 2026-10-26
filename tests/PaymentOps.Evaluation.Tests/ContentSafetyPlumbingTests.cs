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

        EvaluationServices.AppConfiguration(new Uri("https://cs.invalid/"), "https://cognitiveservices.azure.com/.default")(services);

        var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        Assert.Equal("https://cs.invalid/", configuration["PaymentOps:ContentSafety:Endpoint"]);
        Assert.Equal("https://cognitiveservices.azure.com/.default", configuration["PaymentOps:Foundry:TokenScope"]);
    }

    [Fact]
    public void Without_an_endpoint_no_configuration_is_added()
    {
        var services = new ServiceCollection();

        EvaluationServices.AppConfiguration(null, "scope")(services);

        Assert.Null(services.BuildServiceProvider().GetService<IConfiguration>());
    }

    [Fact]
    public async Task A_non_https_content_safety_endpoint_exits_2()
    {
        var exit = await InvestigationEvalCommand.RunAsync(
            [.. CommandTestArgs(), "--content-safety", "http://cs.invalid/"], (_, _) => throw new InvalidOperationException("must not run"), _ => null, TimeSpan.Zero);

        Assert.Equal(2, exit);
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

    private static string[] CommandTestArgs() =>
    [
        "--foundry", "https://foundry.invalid/", "--search", "https://search.invalid", "--scope", "https://cognitiveservices.azure.com/.default",
        "--ai", Path.Combine(RepositoryRoot.FullPath, "ai"), "--sources", Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src"),
        "--dataset", Path.Combine(RepositoryRoot.FullPath, "eval", "datasets", "agent-tasks.json"), "--runs", "1", "--system", "agent",
        "--pricing", Path.Combine(RepositoryRoot.FullPath, "eval", "pricing.json"), "--out", Path.GetTempFileName(), "--summary", Path.GetTempFileName(),
    ];
}
