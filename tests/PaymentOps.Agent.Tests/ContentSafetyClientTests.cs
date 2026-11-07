using System.Net;
using System.Text;

using Microsoft.Extensions.Time.Testing;

using PaymentOps.Agent.Safety;
using PaymentOps.Tests.Shared;

namespace PaymentOps.Agent.Tests;

public sealed class ContentSafetyClientTests
{
    private static readonly Uri Endpoint = new("https://cs.invalid/");

    /// <summary>Answers with the scripted statuses in order, the last one repeating, and counts the calls.</summary>
    private sealed class ScriptedStatusHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        public TaskCompletionSource FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            FirstCall.TrySetResult();
            var status = statuses[Math.Min(call, statuses.Length) - 1];
            var response = new HttpResponseMessage(status) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(response);
        }
    }

    private static ContentSafetyClient Client(HttpMessageHandler handler, TimeProvider time) =>
        new(new HttpClient(handler), Endpoint, "https://cognitiveservices.azure.com/.default", new StaticTokenCredential(), time);

    private static async Task<bool> PostAnswered(ScriptedStatusHandler handler)
    {
        var time = new FakeTimeProvider();
        var post = Client(handler, time).PostAsync("contentsafety/text:analyze", new { text = "t" }, CancellationToken.None);
        await handler.FirstCall.Task;
        while (!post.IsCompleted)
        {
            // Let the client reach its wait, then pass the service's Retry-After.
            await Task.Delay(10);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        using var body = await post;
        return body is not null;
    }

    // A single throttled call would leave the text unscreened, which withholds it from the model.
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_throttled_or_passing_failure_is_retried_once_after_the_services_wait(HttpStatusCode first)
    {
        var handler = new ScriptedStatusHandler(first, HttpStatusCode.OK);

        Assert.True(await PostAnswered(handler));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_failure_that_persists_is_tried_twice_then_unanswered()
    {
        var handler = new ScriptedStatusHandler(HttpStatusCode.TooManyRequests);

        Assert.False(await PostAnswered(handler));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_rejected_request_is_not_retried()
    {
        var handler = new ScriptedStatusHandler(HttpStatusCode.BadRequest);

        Assert.False(await PostAnswered(handler));
        Assert.Equal(1, handler.Calls);
    }
}
