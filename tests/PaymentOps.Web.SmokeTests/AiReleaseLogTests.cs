using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PaymentOps.Web.SmokeTests;

public sealed class AiReleaseLogTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task The_release_is_logged_once_the_host_has_started()
    {
        // Telemetry exporters only send what's logged after the host starts; earlier lines reach the console only.
        // The marker is registered last, so it starts after the telemetry service.
        var capture = new ReleaseLogCapture();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(capture);
            services.AddHostedService(_ => new StartMarker(capture));
        }));

        _ = app.Server;

        // The started notification can run after the test server is returned.
        var entry = await capture.ReleaseLoaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(entry.HostStarted);
        Assert.Contains(entry.State, kv => kv.Key == "ActiveChatDeployment" && Equals(kv.Value, "chat-blue"));
    }

    private sealed record Entry(bool HostStarted, IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class StartMarker(ReleaseLogCapture capture) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            capture.HostStarted = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ReleaseLogCapture : ILoggerProvider, ILogger
    {
        private const int ReleaseLoadedEventId = 1001;

        public volatile bool HostStarted;

        public TaskCompletionSource<Entry> ReleaseLoaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == ReleaseLoadedEventId)
            {
                ReleaseLoaded.TrySetResult(new Entry(HostStarted, state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
            }
        }

        public void Dispose()
        {
        }
    }
}
