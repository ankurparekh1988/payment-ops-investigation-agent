using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Running;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

/// <summary>
/// The app keeps screening verdicts for the life of its process, so a run's cases share one cache, as the app's
/// investigations would; each run starts empty, so runs stay independent measurements.
/// </summary>
public sealed class RunScreenCacheTests
{
    // Internal to the agent, which this project can't see; looked up by name rather than widening the app's surface.
    private static readonly Type ScreenCacheType = typeof(IUntrustedTextScreen).Assembly.GetType("PaymentOps.Agent.Safety.ScreenCache", throwOnError: true)!;

    [Fact]
    public async Task Cases_in_a_run_share_the_screen_cache_and_each_run_starts_with_its_own()
    {
        var caches = new List<object>();
        void Configure(IServiceCollection services)
        {
            // A blocked question ends the case before the model; resolving its screen shows the cache that case used.
            Screening(questions: new ScriptedQuestionScreen(QuestionVerdict.Harmful))(services);
            EvaluationServices.Wrap<IQuestionScreen>(services, (sp, screen) =>
            {
                caches.Add(sp.GetRequiredService(ScreenCacheType));
                return screen;
            });
        }

        var runner = new AgentCaseRunner(Release, Endpoints, new UnusedCredential(), _ => Model(), _ => new FixedKnowledge(GeneralSection), Configure, TimeSpan.Zero);

        await RunPlan.RunAsync(runner, [Case("a"), Case("b")], runs: 2, TimeSpan.Zero, new EvaluationDiagnostics(), CancellationToken.None);

        Assert.Equal(4, caches.Count);
        Assert.Same(caches[0], caches[1]);
        Assert.Same(caches[2], caches[3]);
        Assert.NotSame(caches[0], caches[2]);
    }
}
