using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Safety;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

public sealed class QuestionScreeningTests
{
    private const string Question = "Why was M-1042's payout late yesterday?";

    private static ScriptedChatClient Investigates() => new(_ => Call(ToolNames.SubmitAssessment, Submission()));

    public static TheoryData<QuestionVerdict, NotAssessedReason> Blocks() => new()
    {
        { QuestionVerdict.Harmful, NotAssessedReason.Blocked },
        { QuestionVerdict.Attack, NotAssessedReason.Blocked },
        { QuestionVerdict.SelfHarm, NotAssessedReason.BlockedSelfHarm },
        { QuestionVerdict.Unavailable, NotAssessedReason.Unscreened },
    };

    [Theory]
    [MemberData(nameof(Blocks))]
    public async Task A_question_the_screen_does_not_pass_never_reaches_the_model(QuestionVerdict verdict, NotAssessedReason reason)
    {
        using var fixture = new AgentFixture();
        var model = Investigates();

        var result = await fixture.Runner(model, questionScreen: new ScriptedQuestionScreen(verdict)).InvestigateAsync(Question, CancellationToken.None);

        Assert.Equal(reason, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Empty(model.Requests);
    }

    // History is context for the agent, never for the screen: each question is judged on its own words.
    [Fact]
    public async Task Only_the_question_itself_is_screened_once_before_the_agent_runs()
    {
        using var fixture = new AgentFixture();
        var screen = ScriptedQuestionScreen.AllClean;
        var model = new ScriptedChatClient(_ => Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }), _ => Call(ToolNames.SubmitAssessment, Submission()));
        ChatMessage[] history = [new(ChatRole.User, "An earlier question."), new(ChatRole.Assistant, "An earlier answer.")];

        var result = await fixture.Runner(model, questionScreen: screen).InvestigateAsync(Question, history, null, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<AssessmentResult.Assessed>(result);
        Assert.Equal([Question], screen.Screened);
    }

    [Fact]
    public async Task A_cancelled_screen_ends_as_cancelled()
    {
        using var fixture = new AgentFixture();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await fixture.Runner(Investigates()).InvestigateAsync(Question, cancelled.Token);

        Assert.Equal(NotAssessedReason.Cancelled, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    [Fact]
    public async Task The_log_says_what_was_found_never_the_question()
    {
        using var fixture = new AgentFixture();
        var logger = new RecordingLogger<InvestigationAgentRunner>();

        await fixture.Runner(Investigates(), logger: logger, questionScreen: new ScriptedQuestionScreen(QuestionVerdict.SelfHarm)).InvestigateAsync(Question, CancellationToken.None);

        var entry = Assert.Single(logger.Entries, e => e.Message.Contains("not investigated", StringComparison.Ordinal));
        Assert.Contains("SelfHarm", entry.Message, StringComparison.Ordinal);
        Assert.All(logger.Entries, e => Assert.DoesNotContain("M-1042", e.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void The_question_screen_is_registered_from_the_content_safety_endpoint()
    {
        using var configured = Registered("https://cs.example/");
        using var missing = Registered(null);

        Assert.IsType<ContentSafetyQuestionScreen>(configured.GetRequiredService<IQuestionScreen>());
        Assert.IsType<UnavailableQuestionScreen>(missing.GetRequiredService<IQuestionScreen>());
    }

    private static ServiceProvider Registered(string? endpoint)
    {
        var services = InvestigationRegistrationTests.Services();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ContentSafetyOptions.EndpointKey] = endpoint }).Build());
        return services.BuildServiceProvider();
    }
}
