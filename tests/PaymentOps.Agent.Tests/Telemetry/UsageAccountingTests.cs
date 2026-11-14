using System.ClientModel;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Agent.Tests.Telemetry;

/// <summary>
/// What a turn used, kept apart by what it is: requests sent (retries included), Content Safety text records billed,
/// model tokens, and cache hits that sent nothing. The classifier is a model, never a Content Safety record.
/// </summary>
public sealed class UsageAccountingTests
{
    private static readonly Uri ContentSafety = new("https://cs.invalid/");
    private const string Scope = "https://cognitiveservices.azure.com/.default";

    /// <summary>Answers with the scripted statuses in order, the last repeating; a success is a clean Prompt Shields answer.</summary>
    private sealed class ScriptedContentSafety(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Interlocked.Increment(ref _calls), statuses.Length) - 1];
            if (status != HttpStatusCode.OK)
            {
                var failed = new HttpResponseMessage(status);
                failed.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return failed;
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var documents = body.RootElement.GetProperty("documents").GetArrayLength();
            var answer = JsonSerializer.Serialize(new { documentsAnalysis = Enumerable.Repeat(new { attackDetected = false }, documents) });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Answers model requests with the scripted statuses in order; a success is a short completion with usage.</summary>
    private sealed class ScriptedModel(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Interlocked.Increment(ref _calls), statuses.Length) - 1];
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    status == HttpStatusCode.OK
                        ? """{"id":"c1","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}"""
                        : """{"error":{"code":"server_error","message":"x"}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            response.Headers.Add("retry-after-ms", "0");
            return Task.FromResult(response);
        }
    }

    private static PromptShieldsScreen Shields(HttpMessageHandler handler, ScreenCache? cache = null) =>
        new(new HttpClient(new RequestCountingHandler { InnerHandler = handler }), ContentSafety, Scope, new StaticTokenCredential(), cache ?? new ScreenCache());

    // A unique deployment name keeps measurements from tests running alongside apart.
    private static string Deployment() => "d-" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(7500, 8)]
    public void Records_are_counted_per_thousand_characters(int length, int records) =>
        Assert.Equal(records, TextRecords.For(new string('a', length)));

    [Fact]
    public void Records_count_unicode_code_points_not_utf16_units() =>
        Assert.Equal(1, TextRecords.For(string.Concat(Enumerable.Repeat("😀", 600))));

    [Fact]
    public async Task Each_document_is_billed_by_its_own_length()
    {
        using var usage = TurnUsage.Begin();

        await Shields(new ScriptedContentSafety(HttpStatusCode.OK)).ScreenAsync([new string('a', 300), new string('b', 2100)], CancellationToken.None);

        Assert.Equal(4, usage.Records["prompt_shields"]);
    }

    [Fact]
    public async Task Retries_are_requests_but_only_successes_are_records()
    {
        using var usage = TurnUsage.Begin();

        await Shields(new ScriptedContentSafety(HttpStatusCode.TooManyRequests, HttpStatusCode.OK)).ScreenAsync(["text"], CancellationToken.None);

        Assert.Equal(2, usage.HttpRequests["prompt_shields"]);
        Assert.Equal(1, usage.Records["prompt_shields"]);
        Assert.True(usage.Complete);
    }

    [Fact]
    public async Task A_failed_screen_bills_no_record()
    {
        using var usage = TurnUsage.Begin();

        await Shields(new ScriptedContentSafety(HttpStatusCode.BadRequest)).ScreenAsync(["text"], CancellationToken.None);

        Assert.Equal(1, usage.HttpRequests["prompt_shields"]);
        Assert.False(usage.Records.ContainsKey("prompt_shields"));
    }

    [Fact]
    public async Task A_cache_hit_sends_nothing()
    {
        var cache = new ScreenCache();
        await Shields(new ScriptedContentSafety(HttpStatusCode.OK), cache).ScreenAsync(["text"], CancellationToken.None);
        using var usage = TurnUsage.Begin();

        await Shields(new ScriptedContentSafety(HttpStatusCode.OK), cache).ScreenAsync(["text"], CancellationToken.None);

        Assert.False(usage.HttpRequests.ContainsKey("prompt_shields"));
        Assert.Equal(1, usage.CacheHits["prompt_shields"]);
    }

    [Fact]
    public async Task A_request_that_never_answers_makes_usage_incomplete()
    {
        using var usage = TurnUsage.Begin();
        var http = new HttpClient(new RequestCountingHandler { InnerHandler = new ThrowingHandler() });

        await new ContentSafetyClient(http, ContentSafety, Scope, new StaticTokenCredential()).PostAsync("contentsafety/text:analyze", new { text = "t" }, records: 1, CancellationToken.None);

        Assert.Equal(1, usage.HttpRequests["text_analysis"]);
        Assert.False(usage.Complete);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection reset");
    }

    [Fact]
    public async Task Tokens_from_each_response_are_added_to_the_turn()
    {
        var deployment = Deployment();
        var measured = MeasuredTokens(deployment);
        var model = new UsageCountingChatClient(new ScriptedChatClient(_ => Usage(120, 30), _ => Usage(120, 30)), deployment);
        using var usage = TurnUsage.Begin();

        await model.GetResponseAsync("q");
        await model.GetResponseAsync("q");

        Assert.Equal((240, 60), usage.Tokens[deployment]);
        Assert.Equal(240, measured.Input);
    }

    [Fact]
    public async Task A_response_without_usage_makes_the_turn_incomplete_but_keeps_counts()
    {
        var deployment = Deployment();
        var model = new UsageCountingChatClient(new ScriptedChatClient(_ => Usage(10, 2), _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "x"))), deployment);
        using var usage = TurnUsage.Begin();

        await model.GetResponseAsync("q");
        await model.GetResponseAsync("q");

        Assert.Equal((10, 2), usage.Tokens[deployment]);
        Assert.False(usage.Complete);
    }

    [Fact]
    public async Task Outside_a_turn_usage_is_only_metered()
    {
        var deployment = Deployment();
        var measured = MeasuredTokens(deployment);

        await new UsageCountingChatClient(new ScriptedChatClient(_ => Usage(5, 1)), deployment).GetResponseAsync("q");

        Assert.Null(TurnUsage.Current);
        Assert.Equal(5, measured.Input);
    }

    [Fact]
    public void Production_model_options_keep_the_SDK_retry_and_transport()
    {
        var options = ModelClient.Options(new Uri("https://foundry.invalid/"), Deployment(), handler: null);

        Assert.Null(options.RetryPolicy);
        Assert.Null(options.Transport);
    }

    [Fact]
    public async Task Model_retries_are_counted_and_still_happen()
    {
        var deployment = Deployment();
        var model = ModelClient.Create(new Uri("https://foundry.invalid/"), Scope, new StaticTokenCredential(), deployment, new ScriptedModel(HttpStatusCode.TooManyRequests, HttpStatusCode.OK));
        using var usage = TurnUsage.Begin();

        var response = await model.GetResponseAsync("q");

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, usage.HttpRequests["model"]);
    }

    [Fact]
    public async Task A_model_failure_is_unchanged_by_counting()
    {
        var model = ModelClient.Create(new Uri("https://foundry.invalid/"), Scope, new StaticTokenCredential(), Deployment(), new ScriptedModel(HttpStatusCode.InternalServerError));
        using var usage = TurnUsage.Begin();

        await Assert.ThrowsAsync<ClientResultException>(() => model.GetResponseAsync("q"));
        Assert.Equal(2, usage.HttpRequests["model"]);
    }

    [Fact]
    public async Task The_classifier_is_tokens_not_records()
    {
        var deployment = Deployment();
        var classifier = new HarmClassifier(new UsageCountingChatClient(new ScriptedChatClient(_ => Usage(50, 5, "{\"categories\":[]}")), deployment), "instructions");
        using var usage = TurnUsage.Begin();

        await classifier.ClassifyAsync("text", CancellationToken.None);

        Assert.Equal((50, 5), usage.Tokens[deployment]);
        Assert.Empty(usage.Records);
    }

    private static ChatResponse Usage(long input, long output, string text = "x") =>
        new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output } };

    private sealed class Measured
    {
        public long Input;
    }

    private static Measured MeasuredTokens(string deployment)
    {
        var measured = new Measured();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AgentTelemetry.Name && instrument.Name == "paymentops.model.tokens")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var mine = false;
            var input = false;
            foreach (var tag in tags)
            {
                mine |= tag.Key == "deployment" && Equals(tag.Value, deployment);
                input |= tag.Key == "direction" && Equals(tag.Value, "input");
            }

            if (mine && input)
            {
                Interlocked.Add(ref measured.Input, value);
            }
        });
        listener.Start();
        return measured;
    }
}
