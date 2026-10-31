using System.ClientModel;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Challenger;
using PaymentOps.Evaluation.Running;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

/// <summary>
/// The agent and the fixed-workflow challenger end a failed turn the same way, so the kill test compares planning,
/// not failure handling. Each failure is raised by the model on the first request either system makes.
/// </summary>
public sealed class TurnOutcomeParityTests
{
    public enum Failure
    {
        CallerCancelled,
        TurnTimeout,
        ModelUnavailable,
        Unexpected,
        PromptFiltered,
        CompletionFiltered,
    }

    public static TheoryData<Failure, string> Failures => new()
    {
        { Failure.CallerCancelled, "not-assessed:cancelled" },
        { Failure.TurnTimeout, "not-assessed:limit-reached" },
        { Failure.ModelUnavailable, "not-assessed:model-unavailable" },
        { Failure.Unexpected, "not-assessed:assessment-unverified" },
        { Failure.PromptFiltered, "not-assessed:blocked" },
        { Failure.CompletionFiltered, "not-assessed:blocked" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task The_agent_and_the_fixed_workflow_end_a_failed_turn_alike(Failure failure, string outcome)
    {
        var agent = await RunAsync(failure, (model, configure) => new AgentCaseRunner(Release, Endpoints, new UnusedCredential(), model, _ => new FixedKnowledge(GeneralSection), configure));
        var challenger = await RunAsync(failure, (model, configure) => new FixedWorkflowCaseRunner(Release, Endpoints, new UnusedCredential(), model, _ => new FixedKnowledge(GeneralSection), configure));

        Assert.Equal(outcome, agent);
        Assert.Equal(outcome, challenger);
    }

    // Each system gets its own caller, so one's cancel can't reach the other's run.
    private static async Task<string> RunAsync(Failure failure, Func<Func<IChatClient, IChatClient>, Action<IServiceCollection>, ICaseRunner> runner)
    {
        using var caller = new CancellationTokenSource();
        var run = await runner(_ => new FailingModel(failure, caller), Configure).RunAsync([Case()], caller.Token);
        return Assert.Single(run.Results).Outcome;
    }

    // A short turn timeout, so the timeout case ends in a moment rather than a minute.
    private static void Configure(IServiceCollection services) =>
        services.Configure<InvestigationLimits>(limits => limits.TurnTimeout = TimeSpan.FromMilliseconds(200));

    // Fails the first request in the way the case names; the caller's cancel is made from inside the request.
    private sealed class FailingModel(Failure failure, CancellationTokenSource caller) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            switch (failure)
            {
                case Failure.CallerCancelled:
                    await caller.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    break;
                case Failure.TurnTimeout:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    break;
                case Failure.ModelUnavailable:
                    throw new HttpRequestException("The model service didn't answer.");
                case Failure.Unexpected:
                    throw new InvalidOperationException("A bug.");
                case Failure.PromptFiltered:
                    throw new ClientResultException(new FakePipelineResponse(400, "{\"error\":{\"code\":\"content_filter\"}}"));
                case Failure.CompletionFiltered:
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)) { FinishReason = ChatFinishReason.ContentFilter };
            }

            throw new InvalidOperationException("Unreachable.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
