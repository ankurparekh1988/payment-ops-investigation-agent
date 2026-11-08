using System.Diagnostics;

using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

/// <summary>An attempt the environment spoiled costs that case one retry; it never costs the run, and never touches what's scored.</summary>
public sealed class CaseRetryTests
{
    private static readonly InvestigationCase[] Cases = [Case("a"), Case("b"), Case("c")];

    private static Func<CaseAttempt> Spoiled(string id, string failure = InfrastructureFailures.ScreeningUnavailable, int tickets = 0) =>
        () => new CaseAttempt(new CaseResult(id, "assessed") { InfrastructureFailure = failure }, tickets);

    private static Func<CaseAttempt> Scored(string id, string outcome = "assessed", int tickets = 0) =>
        () => new CaseAttempt(new CaseResult(id, outcome), tickets);

    private static Task<IReadOnlyList<RunOutcome>> Run(ICaseRunner runner, IReadOnlyList<InvestigationCase> cases, int runs = 1, TimeSpan? delay = null, EvaluationDiagnostics? diagnostics = null) =>
        RunPlan.RunAsync(runner, cases, runs, delay ?? TimeSpan.Zero, diagnostics ?? new EvaluationDiagnostics(), CancellationToken.None);

    [Fact]
    public async Task A_spoiled_attempt_retries_only_that_case_and_earlier_cases_arent_rerun()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["b"] = [Spoiled("b"), Scored("b")] });

        var run = Assert.Single(await Run(runner, Cases));

        Assert.Equal(["a", "b", "b", "c"], runner.Asked);
        Assert.Equal(["a", "b", "c"], run.Results.Select(r => r.CaseId));
    }

    [Fact]
    public async Task A_successful_retry_is_the_one_result_scored_and_only_its_tickets_count()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["b"] = [Spoiled("b", tickets: 5), Scored("b", tickets: 1)] });

        var run = Assert.Single(await Run(runner, Cases));

        Assert.Null(Assert.Single(run.Results, r => r.CaseId == "b").InfrastructureFailure);
        Assert.Equal(1, run.TicketsInStore);
    }

    [Fact]
    public async Task A_case_spoiled_twice_could_not_evaluate_and_the_rest_never_runs()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["b"] = [Spoiled("b", InfrastructureFailures.ModelBusy), Spoiled("b")] });

        var ex = await Assert.ThrowsAsync<CouldNotEvaluateException>(() => Run(runner, Cases));

        Assert.Equal(["a", "b", "b"], runner.Asked);
        Assert.Contains("b", ex.Message, StringComparison.Ordinal);
        Assert.Contains(InfrastructureFailures.ModelBusy, ex.Message, StringComparison.Ordinal);
        Assert.Contains(InfrastructureFailures.ScreeningUnavailable, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("refused")]
    [InlineData("not-assessed:assessment-unverified")]
    [InlineData("not-assessed:limit-reached")]
    [InlineData("not-assessed:blocked")]
    public async Task An_outcome_of_the_system_is_scored_and_never_retried(string outcome)
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["b"] = [Scored("b", outcome), Scored("b")] });

        var run = Assert.Single(await Run(runner, Cases));

        Assert.Equal(["a", "b", "c"], runner.Asked);
        Assert.Equal(outcome, Assert.Single(run.Results, r => r.CaseId == "b").Outcome);
    }

    [Fact]
    public async Task Three_case_retries_in_a_run_are_allowed()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>
        {
            ["a"] = [Spoiled("a"), Scored("a")],
            ["b"] = [Spoiled("b"), Scored("b")],
            ["c"] = [Spoiled("c"), Scored("c")],
        });

        Assert.Equal(3, Assert.Single(await Run(runner, Cases)).Results.Count);
    }

    // Many cases each needing a retry is an outage, not bad luck: the evaluation stops rather than limp through it.
    [Fact]
    public async Task A_fourth_case_retry_in_a_run_could_not_evaluate()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>
        {
            ["a"] = [Spoiled("a"), Scored("a")],
            ["b"] = [Spoiled("b"), Scored("b")],
            ["c"] = [Spoiled("c"), Scored("c")],
            ["d"] = [Spoiled("d"), Scored("d")],
            ["e"] = [Scored("e")],
        });

        var ex = await Assert.ThrowsAsync<CouldNotEvaluateException>(() => Run(runner, [.. Cases, Case("d"), Case("e")]));

        Assert.Contains("more than 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("case d", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("e", runner.Asked);
        Assert.Single(runner.Asked, id => id == "d");
    }

    [Fact]
    public async Task The_retry_cap_is_per_run()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>
        {
            ["a"] = [Spoiled("a"), Scored("a"), Spoiled("a"), Scored("a")],
            ["b"] = [Spoiled("b"), Scored("b"), Spoiled("b"), Scored("b")],
            ["c"] = [Spoiled("c"), Scored("c"), Spoiled("c"), Scored("c")],
        });

        Assert.Equal(2, (await Run(runner, Cases, runs: 2)).Count);
    }

    [Fact]
    public async Task The_retry_waits_for_the_delay_first()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["a"] = [Spoiled("a"), Scored("a")] });
        var timer = Stopwatch.StartNew();

        await Run(runner, [Case("a")], delay: TimeSpan.FromMilliseconds(100));

        Assert.True(timer.ElapsedMilliseconds >= 90, $"waited {timer.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task An_environment_failure_raised_by_the_runner_is_retried_too()
    {
        var thrown = false;
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>
        {
            ["a"] = [() => { thrown = true; throw new HttpRequestException("503"); }, Scored("a")],
        });

        Assert.Single(Assert.Single(await Run(runner, [Case("a")])).Results);
        Assert.True(thrown);
    }

    [Fact]
    public async Task A_bug_in_the_harness_is_never_retried()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>
        {
            ["a"] = [() => throw new InvalidOperationException("bug"), Scored("a")],
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(runner, [Case("a")]));
        Assert.Single(runner.Asked);
    }

    [Fact]
    public async Task Three_runs_score_every_case_once_in_each()
    {
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["b"] = [Spoiled("b"), Scored("b")] });

        var runs = await Run(runner, Cases, runs: 3);

        Assert.Equal(3, runs.Count);
        Assert.All(runs, run => Assert.Equal(["a", "b", "c"], run.Results.Select(r => r.CaseId)));
        Assert.Equal(10, runner.Asked.Count);
    }

    // What a discarded attempt did can't reach a metric or its denominator: the scores equal those of a run that never failed.
    [Fact]
    public async Task A_discarded_attempt_leaves_every_metric_and_denominator_unchanged()
    {
        var cases = new LoadedCases([.. Cases], new Dictionary<ExpectedSection, string>(), new Dictionary<string, string>(StringComparer.Ordinal) { ["restricted-section"] = "restricted" });
        var spoiled = new CaseResult("b", "assessed")
        {
            InfrastructureFailure = InfrastructureFailures.ScreeningUnavailable,
            Calls = [new ToolCallRecord("get_merchant_profile", new Dictionary<string, string?>())],
            RetrievedSections = [new SectionHit("restricted-section", "restricted")],
        };
        var retried = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["b"] = [() => new CaseAttempt(spoiled, 3), Scored("b")] });
        var clean = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>());

        var withRetry = Metrics.Score(cases, Assert.Single(await Run(retried, Cases)));
        var without = Metrics.Score(cases, Assert.Single(await Run(clean, Cases)));

        Assert.Equal(without.Values, withRetry.Values);
        Assert.Empty(withRetry.Hard.AuthorizationLeaks);
        Assert.Empty(withRetry.Hard.UnauthorizedStateChanges);
    }

    [Fact]
    public async Task A_retry_gets_fresh_services()
    {
        var failed = false;
        var builds = 0;
        Func<ScriptedChatClient> model = () =>
        {
            builds++;
            return failed
                ? Model(Trajectory(Submission()))
                : new ScriptedChatClient(_ =>
                {
                    failed = true;
                    throw new HttpRequestException("503");
                });
        };

        var run = Assert.Single(await Run(AgentRunner(model), [Case()]));

        Assert.Equal("assessed", Assert.Single(run.Results).Outcome);
        Assert.Equal(2, builds);
    }

    // The retry covers the whole case, so the re-ask rule starts over and the first attempt's second ask isn't scored.
    [Fact]
    public async Task A_retry_repeats_the_whole_case_including_its_second_ask()
    {
        var failed = false;
        Func<ScriptedChatClient> model = () => failed
            ? Model(Trajectory(Submission()))
            : new ScriptedChatClient(_ =>
            {
                failed = true;
                throw new HttpRequestException("503");
            });
        var questions = new ScriptedQuestionScreen(QuestionVerdict.Unavailable, QuestionVerdict.Clean, QuestionVerdict.Clean);

        var result = Assert.Single(Assert.Single(await Run(AgentRunner(model, questions: questions), [Case()])).Results);

        Assert.Equal("assessed", result.Outcome);
        Assert.False(result.Reasked);
        Assert.Equal(3, questions.Screened.Count);
    }

    [Fact]
    public async Task A_question_screened_on_its_second_ask_is_scored_once_without_a_case_retry()
    {
        var questions = new ScriptedQuestionScreen(QuestionVerdict.Unavailable, QuestionVerdict.Clean);
        var diagnostics = new EvaluationDiagnostics();

        var result = Assert.Single(Assert.Single(await Run(AgentRunner(() => Model(Trajectory(Submission())), questions: questions), [Case()], diagnostics: diagnostics)).Results);

        Assert.Equal("assessed", result.Outcome);
        Assert.True(result.Reasked);
        Assert.Equal(2, questions.Screened.Count);
        Assert.Empty(diagnostics.Snapshot().CaseRetries);
    }

    // Still unscreened on the second ask spoils the attempt; the case retry starts over from a first ask of its own.
    [Fact]
    public async Task A_question_unscreened_twice_retries_the_case_and_only_the_retry_is_scored()
    {
        var questions = new ScriptedQuestionScreen(QuestionVerdict.Unavailable, QuestionVerdict.Unavailable, QuestionVerdict.Clean);
        var diagnostics = new EvaluationDiagnostics();

        var result = Assert.Single(Assert.Single(await Run(AgentRunner(() => Model(Trajectory(Submission())), questions: questions), [Case()], diagnostics: diagnostics)).Results);

        Assert.Equal("assessed", result.Outcome);
        Assert.Null(result.InfrastructureFailure);
        Assert.False(result.Reasked);
        Assert.Equal(5, result.ModelRequests);
        Assert.Equal(3, questions.Screened.Count);
        var retry = Assert.Single(diagnostics.Snapshot().CaseRetries);
        Assert.Equal(InfrastructureFailures.ScreeningUnavailable, retry.Cause);
    }

    // A screen down for four asks of one case is an outage: the evaluation stops rather than pass or fail on it.
    [Fact]
    public async Task A_retried_case_unscreened_twice_again_could_not_evaluate()
    {
        var questions = new ScriptedQuestionScreen(QuestionVerdict.Unavailable);

        var ex = await Assert.ThrowsAsync<CouldNotEvaluateException>(() => Run(AgentRunner(() => Model(Trajectory(Submission())), questions: questions), [Case()]));

        Assert.Contains(InfrastructureFailures.ScreeningUnavailable, ex.Message, StringComparison.Ordinal);
        Assert.Equal(4, questions.Screened.Count);
    }

    [Fact]
    public async Task Discarded_unscreened_attempts_leave_every_metric_unchanged()
    {
        var cases = new LoadedCases([.. Cases], new Dictionary<ExpectedSection, string>(), new Dictionary<string, string>(StringComparer.Ordinal) { ["restricted-section"] = "restricted" });
        var unscreenedTwice = new ScriptedQuestionScreen(QuestionVerdict.Unavailable, QuestionVerdict.Unavailable, QuestionVerdict.Clean);

        var withRetry = Metrics.Score(cases, Assert.Single(await Run(AgentRunner(() => Model(Trajectory(Submission())), questions: unscreenedTwice), Cases)));
        var without = Metrics.Score(cases, Assert.Single(await Run(AgentRunner(() => Model(Trajectory(Submission()))), Cases)));

        Assert.Equal(without.Values, withRetry.Values);
        Assert.Empty(withRetry.Hard.AuthorizationLeaks);
        Assert.Empty(withRetry.Hard.UnauthorizedStateChanges);
    }

    [Theory]
    [InlineData(QuestionVerdict.Harmful)]
    [InlineData(QuestionVerdict.SelfHarm)]
    [InlineData(QuestionVerdict.Attack)]
    public async Task A_question_the_screen_blocks_is_scored_and_never_retried(QuestionVerdict verdict)
    {
        var questions = new ScriptedQuestionScreen(verdict);
        var diagnostics = new EvaluationDiagnostics();

        var result = Assert.Single(Assert.Single(await Run(AgentRunner(() => Model(Trajectory(Submission())), questions: questions), [Case()], diagnostics: diagnostics)).Results);

        Assert.StartsWith("not-assessed:blocked", result.Outcome, StringComparison.Ordinal);
        Assert.Null(result.InfrastructureFailure);
        Assert.Single(questions.Screened);
        Assert.Empty(diagnostics.Snapshot().CaseRetries);
    }
}
