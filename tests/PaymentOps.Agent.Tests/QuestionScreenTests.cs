using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Agent.Tests;

public sealed class QuestionScreenTests
{
    private static readonly Uri Endpoint = new("https://cs.invalid/");

    /// <summary>Answers text:analyze and text:shieldPrompt from scripted functions, recording each request.</summary>
    private sealed class ContentSafetyHandler(Func<string, (HttpStatusCode, string)> analyze, Func<string, (HttpStatusCode, string)> shield, TimeSpan delay = default, TimeProvider? time = null)
        : HttpMessageHandler
    {
        public List<(string Path, string? Auth, JsonNode Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            lock (Requests)
            {
                Requests.Add((request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body));
            }

            if (delay != default)
            {
                await Task.Delay(delay, time ?? TimeProvider.System, cancellationToken);
            }

            var (status, json) = request.RequestUri!.AbsolutePath.EndsWith("text:analyze", StringComparison.Ordinal)
                ? analyze(body["text"]!.GetValue<string>())
                : shield(body["userPrompt"]!.GetValue<string>());
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (HttpStatusCode, string) Severities(int hate = 0, int sexual = 0, int violence = 0, int selfHarm = 0) => (HttpStatusCode.OK, JsonSerializer.Serialize(new
    {
        categoriesAnalysis = new object[]
        {
            new { category = "Hate", severity = hate },
            new { category = "Sexual", severity = sexual },
            new { category = "Violence", severity = violence },
            new { category = "SelfHarm", severity = selfHarm },
        },
    }));

    private static (HttpStatusCode, string) Attack(bool detected) =>
        (HttpStatusCode.OK, JsonSerializer.Serialize(new { userPromptAnalysis = new { attackDetected = detected }, documentsAnalysis = Array.Empty<object>() }));

    private static ScriptedChatClient Classifier(params string[] categories) =>
        new(_ => ScriptedChatClient.Text(JsonSerializer.Serialize(new { categories })));

    private static ContentSafetyQuestionScreen Screen(ContentSafetyHandler handler, IChatClient classifier, TimeProvider? time = null) =>
        new(new ContentSafetyClient(new HttpClient(handler), Endpoint, "https://cognitiveservices.azure.com/.default", new StaticTokenCredential()),
            new HarmClassifier(classifier, "Classify."), Microsoft.Extensions.Options.Options.Create(new InvestigationLimits()), time ?? TimeProvider.System);

    private static ContentSafetyHandler Clean() => new(_ => Severities(), _ => Attack(false));

    [Fact]
    public async Task A_clean_question_passes_all_three_checks()
    {
        var handler = Clean();
        var classifier = Classifier();

        Assert.Equal(QuestionVerdict.Clean, await Screen(handler, classifier).ScreenAsync("Why was M-1042 paid late?", CancellationToken.None));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer test-token", r.Auth));
        Assert.Single(classifier.Requests);
    }

    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 0, 6)]
    public void Low_severity_and_above_is_harmful(int hate, int sexual, int violence)
    {
        var verdict = HarmAnalysis.Verdict(new Dictionary<string, int> { ["Hate"] = hate, ["Sexual"] = sexual, ["Violence"] = violence, ["SelfHarm"] = 0 });

        Assert.Equal(QuestionVerdict.Harmful, verdict);
    }

    [Fact]
    public void Severity_below_low_is_clean_and_self_harm_is_its_own_verdict()
    {
        Assert.Equal(QuestionVerdict.Clean, HarmAnalysis.Verdict(new Dictionary<string, int> { ["Hate"] = 1, ["Violence"] = 1 }));
        Assert.Equal(QuestionVerdict.SelfHarm, HarmAnalysis.Verdict(new Dictionary<string, int> { ["Hate"] = 4, ["SelfHarm"] = 2 }));
    }

    [Fact]
    public async Task The_harm_check_asks_for_the_four_categories_at_four_levels()
    {
        var handler = Clean();

        await Screen(handler, Classifier()).ScreenAsync("Question?", CancellationToken.None);

        var analyze = Assert.Single(handler.Requests, r => r.Path.Contains("text:analyze", StringComparison.Ordinal));
        Assert.Equal("/contentsafety/text:analyze?api-version=2024-09-01", analyze.Path);
        Assert.Equal(["Hate", "Sexual", "Violence", "SelfHarm"], analyze.Body["categories"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal("FourSeverityLevels", analyze.Body["outputType"]!.GetValue<string>());
    }

    [Fact]
    public async Task Each_check_alone_blocks()
    {
        Assert.Equal(QuestionVerdict.Harmful, await Screen(new(_ => Severities(hate: 4), _ => Attack(false)), Classifier()).ScreenAsync("q", CancellationToken.None));
        Assert.Equal(QuestionVerdict.Attack, await Screen(new(_ => Severities(), _ => Attack(true)), Classifier()).ScreenAsync("q", CancellationToken.None));
        Assert.Equal(QuestionVerdict.Harmful, await Screen(Clean(), Classifier("violence")).ScreenAsync("q", CancellationToken.None));
        Assert.Equal(QuestionVerdict.SelfHarm, await Screen(Clean(), Classifier("self_harm")).ScreenAsync("q", CancellationToken.None));
    }

    // The notice for self-harm points to support, so it wins whichever check found it.
    [Fact]
    public async Task Self_harm_wins_over_any_other_finding()
    {
        var verdict = await Screen(new(_ => Severities(hate: 4), _ => Attack(true)), Classifier("self_harm")).ScreenAsync("q", CancellationToken.None);

        Assert.Equal(QuestionVerdict.SelfHarm, verdict);
    }

    [Fact]
    public async Task A_check_that_fails_leaves_the_question_unscreened()
    {
        Assert.Equal(QuestionVerdict.Unavailable, await Screen(new(_ => (HttpStatusCode.TooManyRequests, "{}"), _ => Attack(false)), Classifier()).ScreenAsync("q", CancellationToken.None));
        Assert.Equal(QuestionVerdict.Unavailable, await Screen(new(_ => Severities(), _ => (HttpStatusCode.InternalServerError, "{}")), Classifier()).ScreenAsync("q", CancellationToken.None));
        Assert.Equal(QuestionVerdict.Unavailable, await Screen(Clean(), new ScriptedChatClient(_ => ScriptedChatClient.Text("not json"))).ScreenAsync("q", CancellationToken.None));
        Assert.Equal(QuestionVerdict.Unavailable, await Screen(Clean(), Classifier("weather")).ScreenAsync("q", CancellationToken.None));
    }

    // A finding stands even when another check couldn't answer: what was found is blocked, not retried.
    [Fact]
    public async Task A_block_stands_when_another_check_is_unavailable() =>
        Assert.Equal(QuestionVerdict.Harmful, await Screen(new(_ => Severities(violence: 2), _ => (HttpStatusCode.InternalServerError, "{}")), Classifier()).ScreenAsync("q", CancellationToken.None));

    [Fact]
    public async Task A_classifier_call_the_content_filter_rejects_is_a_block()
    {
        var filtered = new ScriptedChatClient(_ => throw new ContentFilteredException());

        Assert.Equal(QuestionVerdict.Harmful, await Screen(Clean(), filtered).ScreenAsync("q", CancellationToken.None));
    }

    [Fact]
    public async Task The_classifier_sees_only_the_question_with_no_tools()
    {
        var classifier = Classifier();

        await Screen(Clean(), classifier).ScreenAsync("Why was M-1042 paid late?", CancellationToken.None);

        var request = Assert.Single(classifier.Requests);
        Assert.Equal([ChatRole.System, ChatRole.User], request.Messages.Select(m => m.Role));
        Assert.Equal("Why was M-1042 paid late?", request.Messages[^1].Text);
        Assert.Empty(request.Tools);
    }

    [Fact]
    public async Task The_checks_share_one_budget_and_a_slow_one_leaves_the_question_unscreened()
    {
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        var handler = new ContentSafetyHandler(_ => Severities(), _ => Attack(false), TimeSpan.FromSeconds(30), time);
        var screening = Screen(handler, Classifier(), time).ScreenAsync("q", CancellationToken.None);

        while (!screening.IsCompleted)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.WhenAny(screening, Task.Delay(TimeSpan.FromMilliseconds(10)));
        }

        Assert.Equal(QuestionVerdict.Unavailable, await screening);
        Assert.True(time.GetUtcNow() - start <= new InvestigationLimits().QuestionScreenTimeout + TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task The_callers_cancellation_still_throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Screen(Clean(), Classifier()).ScreenAsync("q", cancelled.Token));
    }

    [Fact]
    public async Task Without_an_endpoint_every_question_is_unscreened() =>
        Assert.Equal(QuestionVerdict.Unavailable, await new UnavailableQuestionScreen().ScreenAsync("q", CancellationToken.None));
}
