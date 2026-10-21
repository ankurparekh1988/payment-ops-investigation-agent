using System.Diagnostics;
using System.Text.Json;

using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentOps.Agent;
using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Data.Demo;
using PaymentOps.Domain.Security;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Agent;

/// <summary>
/// Runs each seeded task through the same registration and service seam the app uses, against the deployed
/// model and index, and records what the model did. Writes nothing anywhere but the report.
/// </summary>
internal static class AgentRun
{
    public static async Task<AgentRunReport> RunAsync(IReadOnlyList<AgentTask> tasks, AiRelease release, InvestigationEndpoints endpoints, TokenCredential credential)
    {
        var results = new List<AgentTaskResult>();
        foreach (var task in tasks)
        {
            var recorder = new RecordingChatClient();
            using var provider = Services(release, endpoints, credential, new EvaluationUser(task.Access == "restricted"), recorder);
            using var scope = provider.CreateScope();

            var timer = Stopwatch.StartNew();
            var result = await scope.ServiceProvider.GetRequiredService<IInvestigationService>().InvestigateAsync(task.Question, CancellationToken.None);
            results.Add(new AgentTaskResult(task, recorder.Calls, Describe(result), timer.ElapsedMilliseconds, recorder.Requests, recorder.InputTokens, recorder.OutputTokens));
        }

        return new AgentRunReport(results);
    }

    private static string Describe(AssessmentResult result) => result switch
    {
        AssessmentResult.Assessed a => $"{a.Outcome.ToString().ToLowerInvariant()} ({a.RootCause?.Label.ToString().ToLowerInvariant() ?? "no root cause"}, {a.Confidence.ToString().ToLowerInvariant()}, {a.Gaps.Count} gaps)",
        AssessmentResult.NotAssessed n => $"not assessed: {n.Reason}",
        _ => "unknown",
    };

    private static ServiceProvider Services(AiRelease release, InvestigationEndpoints endpoints, TokenCredential credential, IUserContext user, RecordingChatClient recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(release);
        services.AddSingleton<IPromptProvider>(new PromptProvider(release));
        services.AddSingleton(credential);
        services.AddSingleton(user);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions<AnsweringOptions>();
        services.AddDemoData(new ConfigurationBuilder().Build());
        services.AddInvestigation(endpoints);

        // The model client the app would use, observed from outside so the run sees every request and tool call.
        var model = services.Last(d => d.ServiceType == typeof(IChatClient));
        services.Remove(model);
        services.AddSingleton<IChatClient>(sp => recorder.Wrapping((IChatClient)model.ImplementationFactory!(sp)));
        return services.BuildServiceProvider();
    }

    /// <summary>Counts requests and tokens and records each tool call; sees calls, never model text.</summary>
    private sealed class RecordingChatClient() : IChatClient
    {
        private IChatClient? _inner;

        public List<ToolCallRecord> Calls { get; } = [];

        public int Requests { get; private set; }

        public long InputTokens { get; private set; }

        public long OutputTokens { get; private set; }

        public RecordingChatClient Wrapping(IChatClient inner)
        {
            _inner = inner;
            return this;
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests++;
            var response = await _inner!.GetResponseAsync(messages, options, cancellationToken);
            InputTokens += response.Usage?.InputTokenCount ?? 0;
            OutputTokens += response.Usage?.OutputTokenCount ?? 0;
            Calls.AddRange(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
                .Select(call => new ToolCallRecord(call.Name, call.Name == "submit_assessment" ? "{}" : JsonSerializer.Serialize(call.Arguments))));
            return response;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => _inner?.GetService(serviceType, serviceKey);

        public void Dispose() => _inner?.Dispose();
    }

    /// <summary>A signed-in Ops Reader, with or without Restricted knowledge access.</summary>
    private sealed record EvaluationUser(bool CanAccessRestrictedKnowledge) : IUserContext
    {
        public bool IsAuthenticated => true;

        public string UserId => "evaluation";

        public string DisplayName => "Agent evaluation";

        public IReadOnlySet<string> Roles { get; } = OpsRoles.Effective([OpsRoles.Reader]);

        public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
