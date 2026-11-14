using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PaymentOps.Agent.Safety;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// The harm verdicts for tool text live in a registered cache, so whoever shares that registration shares the
/// verdicts: the evaluation gives each run's cases one cache, as the app's process does its investigations.
/// </summary>
public sealed class HarmCacheRegistrationTests
{
    private const string Text = "Settlement files from the processor arrive by 06:00 UTC.";

    private static readonly string[] Categories = ["Hate", "Sexual", "Violence", "SelfHarm"];

    /// <summary>Answers every Content Safety call as clean and counts them by operation.</summary>
    private sealed class CleanContentSafety(params HttpStatusCode[] analyzeStatuses) : HttpMessageHandler
    {
        private int _shields;
        private int _analyses;

        public int Shields => _shields;

        public int Analyses => _analyses;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("text:shieldPrompt", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _shields);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var documents = body.RootElement.GetProperty("documents").GetArrayLength();
                return Json(new { userPromptAnalysis = new { attackDetected = false }, documentsAnalysis = Enumerable.Repeat(new { attackDetected = false }, documents) });
            }

            var call = Interlocked.Increment(ref _analyses);
            var status = analyzeStatuses.Length == 0 ? HttpStatusCode.OK : analyzeStatuses[Math.Min(call, analyzeStatuses.Length) - 1];
            return status == HttpStatusCode.OK
                ? Json(new { categoriesAnalysis = Categories.Select(c => new { category = c, severity = 0 }) })
                : new HttpResponseMessage(status);
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    /// <summary>The classifier's deployment, finding nothing and counting its calls.</summary>
    private sealed class CountingClassifier : IChatClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"categories\":[]}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // One case's services, as the evaluation builds them: the app's registration with the run's caches put in.
    private static ServiceProvider Case(CleanContentSafety contentSafety, CountingClassifier classifier, ScreenCache? shieldsCache = null, ScreenCache? harmCache = null)
    {
        var services = InvestigationRegistrationTests.Services();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ContentSafetyOptions.EndpointKey] = "https://cs.example/" }).Build());
        services.Replace(ServiceDescriptor.KeyedSingleton(InvestigationServiceCollectionExtensions.ScreenHttpKey, new HttpClient(contentSafety)));
        services.Replace(ServiceDescriptor.KeyedSingleton<IChatClient>(HarmClassifier.PromptId, classifier));
        if (shieldsCache is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(shieldsCache));
        }

        if (harmCache is not null)
        {
            services.Replace(ServiceDescriptor.KeyedSingleton(ScreenCache.HarmKey, harmCache));
        }

        return services.BuildServiceProvider();
    }

    private static async Task<ScreenVerdict> ScreenAsync(ServiceProvider provider) =>
        (await provider.GetRequiredService<IUntrustedTextScreen>().ScreenAsync([Text], CancellationToken.None))[0];

    [Fact]
    public async Task Repeated_tool_text_in_a_run_is_analysed_once()
    {
        var contentSafety = new CleanContentSafety();
        var classifier = new CountingClassifier();
        var (shields, harm) = (new ScreenCache(), new ScreenCache());

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var provider = Case(contentSafety, classifier, shields, harm);
            Assert.Equal(ScreenVerdict.Clean, await ScreenAsync(provider));
        }

        Assert.Equal(1, contentSafety.Shields);
        Assert.Equal(1, contentSafety.Analyses);
        Assert.Equal(1, classifier.Calls);
    }

    [Fact]
    public async Task Each_run_analyses_the_text_again()
    {
        var contentSafety = new CleanContentSafety();
        var classifier = new CountingClassifier();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var provider = Case(contentSafety, classifier, new ScreenCache(), new ScreenCache());
            await ScreenAsync(provider);
        }

        Assert.Equal(2, contentSafety.Analyses);
        Assert.Equal(2, classifier.Calls);
    }

    [Fact]
    public void The_prompt_shields_and_harm_caches_are_separate()
    {
        using var provider = Case(new CleanContentSafety(), new CountingClassifier());

        Assert.NotSame(provider.GetRequiredService<ScreenCache>(), provider.GetRequiredKeyedService<ScreenCache>(ScreenCache.HarmKey));
    }

    // A 400 isn't retried, so the first analysis can't answer; that verdict must not stand for the text later.
    [Fact]
    public async Task An_unavailable_verdict_is_never_cached()
    {
        var contentSafety = new CleanContentSafety(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        var classifier = new CountingClassifier();
        var (shields, harm) = (new ScreenCache(), new ScreenCache());

        using (var first = Case(contentSafety, classifier, shields, harm))
        {
            Assert.Equal(ScreenVerdict.Unavailable, await ScreenAsync(first));
        }

        using (var second = Case(contentSafety, classifier, shields, harm))
        {
            Assert.Equal(ScreenVerdict.Clean, await ScreenAsync(second));
        }

        Assert.Equal(2, contentSafety.Analyses);
    }
}
