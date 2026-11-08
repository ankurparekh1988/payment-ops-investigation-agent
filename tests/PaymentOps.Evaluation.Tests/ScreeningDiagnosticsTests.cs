using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

using Azure.Core;

using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent.Safety;
using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Running;
using PaymentOps.Tests.Shared;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

/// <summary>
/// What the harness can say about screening without touching the app: every request seen from outside, against a real
/// socket handler and the real model SDK, because a stub handler bypasses the diagnostics these counts come from.
/// </summary>
public sealed class ScreeningDiagnosticsTests
{
    private const string Shields = "contentsafety/text:shieldPrompt";
    private const string Harm = "contentsafety/text:analyze";
    private const string Model = "openai/v1/chat/completions";

    private static readonly HttpClient Http = new(new SocketsHttpHandler());

    /// <summary>A tool-text or question screen that does whatever the test says, inside the observed call.</summary>
    private sealed class ActingScreen(Func<CancellationToken, Task> act, ScreenVerdict verdict = ScreenVerdict.Clean, QuestionVerdict question = QuestionVerdict.Clean) : IUntrustedTextScreen, IQuestionScreen
    {
        public async Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken)
        {
            await act(cancellationToken);
            return [.. segments.Select(_ => verdict)];
        }

        public async Task<QuestionVerdict> ScreenAsync(string question1, CancellationToken cancellationToken)
        {
            await act(cancellationToken);
            return question;
        }
    }

    private static Task<HttpResponseMessage> Post(LocalServer server, string path, object? body = null, CancellationToken cancellationToken = default) =>
        Http.PostAsJsonAsync(new Uri(server.Root, path), body ?? new { }, cancellationToken);

    private static Task<IReadOnlyList<ScreenVerdict>> ToolText(EvaluationDiagnostics diagnostics, Func<CancellationToken, Task> act, ScreenVerdict verdict = ScreenVerdict.Clean) =>
        diagnostics.Observe((IUntrustedTextScreen)new ActingScreen(act, verdict)).ScreenAsync(["text"], CancellationToken.None);

    private static Task<QuestionVerdict> Question(EvaluationDiagnostics diagnostics, Func<CancellationToken, Task> act, QuestionVerdict verdict = QuestionVerdict.Clean, TimeSpan? budget = null) =>
        diagnostics.Observe(new ActingScreen(act, question: verdict), budget ?? TimeSpan.FromSeconds(8)).ScreenAsync("the question", CancellationToken.None);

    [Fact]
    public async Task Prompt_shields_and_harm_analysis_are_counted_apart()
    {
        using var server = new LocalServer();
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await ToolText(diagnostics, async token =>
        {
            await Post(server, Shields, cancellationToken: token);
            await Post(server, Harm, cancellationToken: token);
            await Post(server, Harm, cancellationToken: token);
        });

        var seen = diagnostics.Snapshot();
        Assert.Equal(1, seen.PromptShields.Requests);
        Assert.Equal(2, seen.HarmAnalysis.Requests);
        Assert.Equal(0, seen.QuestionClassifier.Requests);
    }

    [Theory]
    [InlineData(429, "throttled")]
    [InlineData(503, "server")]
    [InlineData(500, "server")]
    [InlineData(403, "other")]
    [InlineData(400, "other")]
    public async Task A_failed_response_is_counted_by_its_class(int status, string kind)
    {
        using var server = new LocalServer();
        server.Answer(Harm, status);
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await ToolText(diagnostics, token => Post(server, Harm, cancellationToken: token));

        var harm = diagnostics.Snapshot().HarmAnalysis;
        Assert.Equal(1, harm.Requests);
        Assert.Equal(kind == "throttled" ? 1 : 0, harm.Throttled);
        Assert.Equal(kind == "server" ? 1 : 0, harm.ServerErrors);
        Assert.Equal(kind == "other" ? 1 : 0, harm.OtherFailures);
    }

    [Fact]
    public async Task A_request_cut_off_by_the_budget_is_timed_out()
    {
        using var server = new LocalServer();
        server.Answer(Harm, 200, delay: TimeSpan.FromSeconds(5));
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await ToolText(diagnostics, async _ => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Post(server, Harm, cancellationToken: budget.Token)));

        var harm = diagnostics.Snapshot().HarmAnalysis;
        Assert.Equal(1, harm.TimedOut);
        Assert.Equal(0, harm.OtherFailures);
    }

    [Fact]
    public async Task A_connection_that_fails_is_another_failure()
    {
        var closed = new Uri($"http://127.0.0.1:{LocalServer.FreePort()}/");
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await ToolText(diagnostics, async _ => await Assert.ThrowsAsync<HttpRequestException>(() => Http.PostAsJsonAsync(new Uri(closed, Harm), new { })));

        Assert.Equal(1, diagnostics.Snapshot().HarmAnalysis.OtherFailures);
    }

    // The Content Safety client sends the same body object again on its one retry, so that's what marks a retry.
    [Fact]
    public async Task The_same_body_sent_again_is_a_retry_and_a_new_body_isnt()
    {
        using var server = new LocalServer();
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();
        var body = new { text = "x" };

        await ToolText(diagnostics, async token =>
        {
            await Post(server, Shields, body, token);
            await Post(server, Shields, body, token);
            await Post(server, Shields, new { text = "x" }, token);
        });

        var shields = diagnostics.Snapshot().PromptShields;
        Assert.Equal(3, shields.Requests);
        Assert.Equal(1, shields.Retries);
    }

    // Both classifiers call one deployment, so only the screen around the call tells them apart.
    [Fact]
    public async Task Concurrent_classifier_calls_go_to_the_screen_that_made_them_through_the_model_sdk()
    {
        using var server = new LocalServer();
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();
        Func<CancellationToken, Task> classify = token => ModelSdkPost(server, token);

        await Task.WhenAll(
            Question(diagnostics, classify),
            ToolText(diagnostics, classify),
            ToolText(diagnostics, classify));

        var seen = diagnostics.Snapshot();
        Assert.Equal(1, seen.QuestionClassifier.Requests);
        Assert.Equal(2, seen.ToolTextClassifier.Requests);
    }

    // The SDK retries a throttled call itself, so each attempt shows up as a request of its own.
    [Fact]
    public async Task A_classifier_call_the_sdk_retried_shows_each_attempt()
    {
        using var server = new LocalServer();
        server.Answer(Model, 429);
        server.Answer(Model, 200);
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await ToolText(diagnostics, token => ModelSdkPost(server, token));

        var seen = diagnostics.Snapshot().ToolTextClassifier;
        Assert.Equal(2, seen.Requests);
        Assert.Equal(1, seen.Throttled);
    }

    [Fact]
    public async Task A_request_outside_a_screen_isnt_counted()
    {
        using var server = new LocalServer();
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await Post(server, Model);
        await Post(server, Shields);

        var seen = diagnostics.Snapshot();
        Assert.Equal(0, seen.PromptShields.Requests + seen.QuestionClassifier.Requests + seen.ToolTextClassifier.Requests);
    }

    [Fact]
    public async Task One_evaluations_screens_never_count_towards_another()
    {
        using var server = new LocalServer();
        var ours = new EvaluationDiagnostics();
        var theirs = new EvaluationDiagnostics();
        using var listeningOurs = ours.Listen();
        using var listeningTheirs = theirs.Listen();

        await ToolText(theirs, token => Post(server, Shields, cancellationToken: token));

        Assert.Equal(0, ours.Snapshot().PromptShields.Requests);
        Assert.Equal(1, theirs.Snapshot().PromptShields.Requests);
    }

    [Fact]
    public async Task Nothing_is_counted_after_listening_stops()
    {
        using var server = new LocalServer();
        var diagnostics = new EvaluationDiagnostics();
        diagnostics.Listen().Dispose();

        await ToolText(diagnostics, token => Post(server, Shields, cancellationToken: token));

        Assert.Equal(0, diagnostics.Snapshot().PromptShields.Requests);
    }

    [Fact]
    public async Task A_screen_that_answers_counts_only_as_a_screen()
    {
        var diagnostics = new EvaluationDiagnostics();

        await Question(diagnostics, _ => Task.CompletedTask);
        await ToolText(diagnostics, _ => Task.CompletedTask);

        var seen = diagnostics.Snapshot();
        Assert.Equal(new ScreenCounts(1, 0, 0, 0), seen.QuestionScreen);
        Assert.Equal(new ScreenCounts(1, 0, 0, 0), seen.ToolTextScreen);
    }

    // The question screen swallows its own deadline, so running out of time is read from how long it took.
    [Fact]
    public async Task A_question_screen_unavailable_after_its_budget_ran_out_of_time()
    {
        var diagnostics = new EvaluationDiagnostics();

        await Question(diagnostics, token => Task.Delay(50, token), QuestionVerdict.Unavailable, TimeSpan.FromMilliseconds(10));

        Assert.Equal(new ScreenCounts(1, 1, 1, 0), diagnostics.Snapshot().QuestionScreen);
    }

    // A deadline's timer can fire slightly before a stopwatch reaches the same budget.
    [Fact]
    public async Task A_question_screen_unavailable_just_short_of_its_budget_still_ran_out_of_time()
    {
        var diagnostics = new EvaluationDiagnostics();

        await Question(diagnostics, token => Task.Delay(60, token), QuestionVerdict.Unavailable, TimeSpan.FromMilliseconds(120));

        Assert.Equal(new ScreenCounts(1, 1, 1, 0), diagnostics.Snapshot().QuestionScreen);
    }

    [Fact]
    public async Task A_tool_text_screen_cancelled_ran_out_of_time_and_the_cancellation_still_propagates()
    {
        var diagnostics = new EvaluationDiagnostics();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ToolText(diagnostics, _ => Task.FromCanceled(new CancellationToken(canceled: true))));

        Assert.Equal(new ScreenCounts(1, 1, 1, 0), diagnostics.Snapshot().ToolTextScreen);
    }

    [Fact]
    public async Task A_screen_unavailable_with_a_failed_request_is_explained_by_it()
    {
        using var server = new LocalServer();
        server.Answer(Harm, 429);
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await ToolText(diagnostics, token => Post(server, Harm, cancellationToken: token), ScreenVerdict.Unavailable);

        Assert.Equal(new ScreenCounts(1, 1, 0, 0), diagnostics.Snapshot().ToolTextScreen);
    }

    // Every request succeeded and the screen still couldn't answer: a malformed body, a refused sign-in, or no endpoint.
    [Fact]
    public async Task A_screen_unavailable_with_no_failed_request_is_unexplained()
    {
        using var server = new LocalServer();
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        await Question(diagnostics, token => Post(server, Harm, cancellationToken: token), QuestionVerdict.Unavailable);

        Assert.Equal(new ScreenCounts(1, 1, 0, 1), diagnostics.Snapshot().QuestionScreen);
    }

    [Fact]
    public async Task The_verdicts_pass_through_unchanged()
    {
        var diagnostics = new EvaluationDiagnostics();

        Assert.Equal(QuestionVerdict.Attack, await Question(diagnostics, _ => Task.CompletedTask, QuestionVerdict.Attack));
        Assert.Equal([ScreenVerdict.Harmful], await ToolText(diagnostics, _ => Task.CompletedTask, ScreenVerdict.Harmful));
    }

    [Fact]
    public async Task The_apps_own_screens_are_the_ones_observed()
    {
        var diagnostics = new EvaluationDiagnostics();
        using var provider = EvaluationServices.Build(
            Release, Endpoints, new UnusedCredential(), Case(), new CaseRecorder(), null, null,
            diagnostics.Observing(EvaluationServices.AppConfiguration(null, Endpoints.FoundryTokenScope)));

        // No endpoint is configured, so the app's own screens answer Unavailable without a request.
        Assert.Equal(QuestionVerdict.Unavailable, await provider.GetRequiredService<IQuestionScreen>().ScreenAsync("the question", CancellationToken.None));
        Assert.Equal([ScreenVerdict.Unavailable], await provider.GetRequiredService<IUntrustedTextScreen>().ScreenAsync(["text"], CancellationToken.None));

        var seen = diagnostics.Snapshot();
        Assert.Equal(1, seen.QuestionScreen.Screens);
        Assert.Equal(1, seen.ToolTextScreen.Screens);
    }

    [Fact]
    public async Task A_case_retry_records_its_cause_and_what_failed_during_that_attempt_only()
    {
        using var server = new LocalServer();
        server.Answer(Model, 429);
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();

        // An earlier case's failure isn't this attempt's.
        await ToolText(diagnostics, token => Post(server, Model, cancellationToken: token));
        server.Answer(Harm, 503);
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]>
        {
            ["a"] =
            [
                () =>
                {
                    ToolText(diagnostics, token => Post(server, Harm, cancellationToken: token)).GetAwaiter().GetResult();
                    return new CaseAttempt(new CaseResult("a", "assessed") { InfrastructureFailure = InfrastructureFailures.ScreeningUnavailable }, 0);
                },
                () => new CaseAttempt(new CaseResult("a", "assessed"), 0),
            ],
        });

        await RunPlan.RunAsync(runner, [Case("a")], 1, TimeSpan.Zero, diagnostics, CancellationToken.None);

        var retry = Assert.Single(diagnostics.Snapshot().CaseRetries);
        Assert.Equal(1, retry.Run);
        Assert.Equal("a", retry.CaseId);
        Assert.Equal(InfrastructureFailures.ScreeningUnavailable, retry.Cause);
        Assert.Equal(["Content Safety harm analysis: 1 server error"], retry.Seen);
    }

    // The retry that never ran isn't a retry, so what's listed stays within the stated cap.
    [Fact]
    public async Task A_refused_fourth_retry_isnt_listed_as_a_case_retry()
    {
        var diagnostics = new EvaluationDiagnostics();
        var spoiled = (string id) => new Func<CaseAttempt>[]
        {
            () => new CaseAttempt(new CaseResult(id, "assessed") { InfrastructureFailure = InfrastructureFailures.ModelBusy }, 0),
            () => new CaseAttempt(new CaseResult(id, "assessed"), 0),
        };
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["a"] = spoiled("a"), ["b"] = spoiled("b"), ["c"] = spoiled("c"), ["d"] = spoiled("d") });

        await Assert.ThrowsAsync<CouldNotEvaluateException>(() => RunPlan.RunAsync(runner, [Case("a"), Case("b"), Case("c"), Case("d")], 1, TimeSpan.Zero, diagnostics, CancellationToken.None));

        Assert.Equal(["a", "b", "c"], diagnostics.Snapshot().CaseRetries.Select(r => r.CaseId));
    }

    // The retry is what ended the evaluation, so its failures have to be named, apart from every earlier case's.
    [Fact]
    public async Task A_case_spoiled_twice_names_the_failures_of_that_case_only()
    {
        using var server = new LocalServer();
        server.Answer(Model, 429);
        server.Answer(Harm, 503);
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();
        await ToolText(diagnostics, token => Post(server, Model, cancellationToken: token));
        var spoiled = () =>
        {
            ToolText(diagnostics, token => Post(server, Harm, cancellationToken: token)).GetAwaiter().GetResult();
            return new CaseAttempt(new CaseResult("a", "assessed") { InfrastructureFailure = InfrastructureFailures.ScreeningUnavailable }, 0);
        };
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["a"] = [spoiled, spoiled] });

        var ex = await Assert.ThrowsAsync<CouldNotEvaluateException>(() => RunPlan.RunAsync(runner, [Case("a")], 1, TimeSpan.Zero, diagnostics, CancellationToken.None));

        Assert.Contains("Content Safety harm analysis: 2 server errors", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("throttled", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sign_in_failure_is_counted()
    {
        var diagnostics = new EvaluationDiagnostics();
        var credential = new CachedTokenCredential(new FailingTokenCredential(), diagnostics.SignInFailed);

        Assert.Throws<Azure.Identity.AuthenticationFailedException>(() => credential.GetToken(new TokenRequestContext(["scope"]), CancellationToken.None));

        Assert.Equal(1, diagnostics.Snapshot().SignInFailures);
    }

    [Fact]
    public async Task A_sign_in_failure_is_counted_when_fetched_asynchronously()
    {
        var diagnostics = new EvaluationDiagnostics();
        var credential = new CachedTokenCredential(new FailingTokenCredential(), diagnostics.SignInFailed);

        await Assert.ThrowsAsync<Azure.Identity.AuthenticationFailedException>(async () => await credential.GetTokenAsync(new TokenRequestContext(["scope"]), CancellationToken.None));

        Assert.Equal(1, diagnostics.Snapshot().SignInFailures);
    }

    [Fact]
    public void The_description_names_only_what_failed()
    {
        var diagnostics = new EvaluationDiagnostics();
        Assert.Equal("no screening failure was seen", diagnostics.Describe());

        diagnostics.SignInFailed();

        Assert.Equal("sign-in: 1 failure", diagnostics.Describe());

        diagnostics.SignInFailed();

        Assert.Equal("sign-in: 2 failures", diagnostics.Describe());
    }

    // Counts and case ids only: the question, tool text, endpoint and the service's answers stay out of everything written.
    [Fact]
    public async Task The_report_and_summary_hold_counts_and_never_text_or_endpoints()
    {
        using var server = new LocalServer();
        server.Answer(Harm, 429, """{ "error": "SERVICE-ANSWER-TEXT" }""");
        var diagnostics = new EvaluationDiagnostics();
        using var listening = diagnostics.Listen();
        await ToolText(diagnostics, token => Post(server, Harm, new { text = "PLANTED-TOOL-TEXT" }, token));
        diagnostics.CaseRetried(2, "c1", InfrastructureFailures.ScreeningUnavailable, []);

        var report = EvaluationReportTests.Report(EvaluationReportTests.Run()) with { Diagnostics = diagnostics.Snapshot() };
        var written = report.ToJson() + ReportMarkdown.Investigations(report);

        Assert.Contains("\"throttled\": 1", written, StringComparison.Ordinal);
        Assert.Contains("Run 2, case c1: screening-unavailable", written, StringComparison.Ordinal);
        foreach (var secret in new[] { "PLANTED-TOOL-TEXT", "SERVICE-ANSWER-TEXT", server.Root.Authority })
        {
            Assert.DoesNotContain(secret, written, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_report_keeps_its_diagnostics_when_read_back()
    {
        var diagnostics = new EvaluationDiagnostics();
        diagnostics.SignInFailed();
        diagnostics.CaseRetried(1, "c1", InfrastructureFailures.ModelBusy, ["tool-text classifier: 1 throttled"]);
        var report = EvaluationReportTests.Report(EvaluationReportTests.Run()) with { Diagnostics = diagnostics.Snapshot() };
        var path = Temp(report.ToJson());

        var loaded = EvaluationReport.Load(path).Diagnostics!;

        Assert.Equal(1, loaded.SignInFailures);
        Assert.Equal(["tool-text classifier: 1 throttled"], Assert.Single(loaded.CaseRetries).Seen);
    }

    // Reports written before diagnostics existed still read, so thresholds and the kill test keep working on them.
    [Fact]
    public void A_report_without_diagnostics_still_reads()
    {
        var report = EvaluationReportTests.Report(EvaluationReportTests.Run()) with { Diagnostics = null };

        Assert.Null(EvaluationReport.Load(Temp(report.ToJson())).Diagnostics);
    }

    private static (string[] Args, string Out, string Summary) Args()
    {
        var args = CommandTests.Args(CommandTests.Dataset());
        var output = args[Array.IndexOf(args, "--out") + 1];
        File.Delete(output);
        return (args, output, args[Array.IndexOf(args, "--summary") + 1]);
    }

    private static RunOutcome ScoredRun() => new([new CaseResult("c1", "assessed")], 0);

    private static RunOutcome SpoiledRun() => new([new CaseResult("c1", "not-assessed:busy") { InfrastructureFailure = "busy" }], 0);

    [Fact]
    public async Task A_passing_evaluation_reports_its_case_retry()
    {
        var (args, output, summary) = Args();

        var exit = await InvestigationEvalCommand.RunAsync(args, (_, _) => new ScriptedRunner(ScoredRun(), SpoiledRun(), ScoredRun(), ScoredRun()), _ => null, TimeSpan.Zero);

        Assert.Equal(0, exit);
        var retry = Assert.Single(EvaluationReport.Load(output).Diagnostics!.CaseRetries);
        Assert.Equal((2, "c1", "busy"), (retry.Run, retry.CaseId, retry.Cause));
        Assert.Contains("Run 2, case c1: busy", await File.ReadAllTextAsync(summary), StringComparison.Ordinal);
    }

    // Only the screens and sign-in are observed, so a model or Search failure can't be described as no failure at all.
    [Fact]
    public void A_retry_with_nothing_seen_doesnt_claim_no_request_failed()
    {
        var diagnostics = new EvaluationDiagnostics();
        diagnostics.CaseRetried(1, "c1", InfrastructureFailures.ModelUnavailable, []);

        var summary = ReportMarkdown.DidNotFinish("Investigation evaluation", "stopped", diagnostics.Snapshot());

        Assert.Contains("Run 1, case c1: model-unavailable (no screening failure seen)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unexpected_failure_still_leaves_diagnostics_in_the_summary()
    {
        var (args, _, summary) = Args();
        var runner = new ByCaseRunner(new Dictionary<string, Func<CaseAttempt>[]> { ["c1"] = [() => throw new InvalidOperationException("SECRET-DETAIL")] });

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvestigationEvalCommand.RunAsync(args, (_, _) => runner, _ => null, TimeSpan.Zero));

        var written = await File.ReadAllTextAsync(summary);
        Assert.Contains("| Prompt Shields |", written, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", written, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-DETAIL", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_evaluation_that_could_not_finish_leaves_diagnostics_in_the_summary_and_no_report()
    {
        var (args, output, summary) = Args();

        var exit = await InvestigationEvalCommand.RunAsync(args, (_, _) => new ScriptedRunner(ScoredRun(), SpoiledRun(), SpoiledRun()), _ => null, TimeSpan.Zero);

        Assert.Equal(3, exit);
        Assert.False(File.Exists(output));
        var written = await File.ReadAllTextAsync(summary);
        Assert.Contains("## Investigation evaluation: did not finish", written, StringComparison.Ordinal);
        Assert.Contains("Run 2, case c1 was invalid twice", written, StringComparison.Ordinal);
        Assert.Contains("| Prompt Shields |", written, StringComparison.Ordinal);
        Assert.Contains("Run 2, case c1: busy", written, StringComparison.Ordinal);
    }

    // The model SDK's own pipeline: its default transport and retry policy. Its sign-in policy refuses plain http, so
    // that step alone is left out here; the live run covers it.
    private static async Task ModelSdkPost(LocalServer server, CancellationToken cancellationToken)
    {
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 1) });
        using var message = pipeline.CreateMessage();
        message.Apply(new RequestOptions { CancellationToken = cancellationToken });
        message.Request.Method = "POST";
        message.Request.Uri = new Uri(server.Root, Model);
        await pipeline.SendAsync(message);
    }

    /// <summary>A real server on a free local port, answering each path from its own queue; the last answer repeats, and an unscripted path gets 200.</summary>
    private sealed class LocalServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, Queue<Scripted>> _answers = new(StringComparer.Ordinal);

        public LocalServer()
        {
            Root = new Uri($"http://localhost:{FreePort()}/");
            _listener.Prefixes.Add(Root.ToString());
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public Uri Root { get; }

        public static int FreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }

        public void Answer(string path, int status, string body = "{}", TimeSpan? delay = null)
        {
            lock (_answers)
            {
                if (!_answers.TryGetValue("/" + path, out var queue))
                {
                    _answers["/" + path] = queue = new Queue<Scripted>();
                }

                queue.Enqueue(new Scripted(status, body, delay ?? TimeSpan.Zero));
            }
        }

        public void Dispose() => _listener.Close();

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => RespondAsync(context));
            }
        }

        private async Task RespondAsync(HttpListenerContext context)
        {
            var answer = Next(context.Request.Url!.AbsolutePath);
            try
            {
                await Task.Delay(answer.Delay);
                context.Response.StatusCode = answer.Status;
                context.Response.ContentType = "application/json";
                var bytes = System.Text.Encoding.UTF8.GetBytes(answer.Body);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // The caller gave up, or the test ended: nobody's waiting for this answer.
            }
        }

        private Scripted Next(string path)
        {
            lock (_answers)
            {
                return !_answers.TryGetValue(path, out var queue) ? new Scripted(200, "{}", TimeSpan.Zero)
                    : queue.Count > 1 ? queue.Dequeue()
                    : queue.Peek();
            }
        }

        private sealed record Scripted(int Status, string Body, TimeSpan Delay);
    }
}
