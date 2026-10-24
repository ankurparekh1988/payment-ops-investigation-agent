using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Security;
using PaymentOps.Evaluation.Prompts;
using PaymentOps.Tools;

using NotAssessed = PaymentOps.Agent.Contracts.AssessmentResult.NotAssessed;

namespace PaymentOps.Evaluation.Challenger;

/// <summary>
/// The kill test's challenger: the agent with its tool planning taken away. One extraction call scopes the
/// question, a fixed fan-out runs the read tools through the agent's own guard and evidence registry, and one
/// call submits through the agent's validator with its single correction. Nothing else differs.
/// </summary>
internal sealed class FixedWorkflow(
    IChatClient model,
    IEnumerable<IInvestigationTool> tools,
    IInvestigationClock clock,
    IUserContext user,
    IOptions<InvestigationLimits> options,
    IOptions<ToolOptions> toolOptions,
    TimeProvider time,
    IReadOnlyList<KnownProcessor> processors)
{
    public static EvaluationPrompt Prompt { get; } = EvaluationPrompt.Load("prompts/fixed-workflow.md");

    private readonly InvestigationLimits _limits = options.Value;

    public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > _limits.MaxQuestionLength)
        {
            return new NotAssessed(NotAssessedReason.InvalidQuestion);
        }

        if (!user.IsAuthenticated || !user.IsInRole(OpsRoles.Reader))
        {
            return new NotAssessed(NotAssessedReason.NotAuthorized);
        }

        var turn = new TurnState();
        var guard = new ToolGuard(turn, _limits, time, user, progress);
        var readTools = tools.Where(tool => ToolAuthorization.Allows(user, tool.Name)).ToDictionary(tool => tool.Name, guard.Wrap, StringComparer.Ordinal);
        var fanOut = new FanOut(readTools, _limits.MaxToolCalls);
        return await TurnOutcome.RunAsync(
            turn,
            _limits,
            time,
            progress,
            async token =>
            {
                await GatherAsync(question, fanOut, token);
                await AssessAsync(question, fanOut, turn, progress, token);
                return null;
            },
            null,
            cancellationToken);
    }

    // Profiles, then each merchant's batches, then status and errors for every processor involved, then knowledge.
    // Calls in a stage don't depend on each other, so they run together.
    private async Task GatherAsync(string question, FanOut fanOut, CancellationToken cancellationToken)
    {
        var scope = await ScopeExtraction.ExtractAsync(model, question, clock.Today, processors, cancellationToken);
        var (start, end) = ScopeExtraction.Window(scope, clock.Today);
        (string, string)[] window = [("start_date", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("end_date", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))];
        if (scope.MerchantIds.Count > 0)
        {
            var profiles = await fanOut.RunAsync([.. scope.MerchantIds.Select(m => (ToolNames.MerchantProfile, Args([("merchant_id", m)])))], cancellationToken);
            await fanOut.RunAsync(
                [.. scope.MerchantIds.Select(m => (ToolNames.SettlementBatches, Args([("merchant_id", m), .. window])))],
                cancellationToken);
            var routed = scope.ProcessorIds.Concat(profiles.SelectMany(RoutedProcessors)).Distinct(StringComparer.Ordinal).ToList();
            await fanOut.RunAsync(
                [.. routed.SelectMany(p => new[]
                {
                    (ToolNames.ProcessorStatus, Args([("processor_id", p), .. window])),
                    (ToolNames.ErrorEvents, Args([("processor_id", p), .. window])),
                })],
                cancellationToken);
        }

        var query = question.Trim();
        query = query.Length > toolOptions.Value.MaxQueryLength ? query[..toolOptions.Value.MaxQueryLength] : query;
        await fanOut.RunAsync([(ToolNames.KnowledgeSearch, Args([("query", query)]))], cancellationToken, last: true);
    }

    // Every result in context and only submit_assessment offered; one more request corrects a rejected submission.
    private async Task AssessAsync(string question, FanOut fanOut, TurnState turn, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            Instructions = $"{Prompt.Body}\n\nNow: {clock.Now:yyyy-MM-dd HH:mm} UTC. Today is {clock.Today:yyyy-MM-dd}.",
            Tools = [new SubmitAssessmentFunction(turn, clock.Today, progress)],
            ToolMode = ChatToolMode.RequireSpecific(ToolNames.SubmitAssessment),
            MaxOutputTokens = _limits.MaxOutputTokens,
            AllowMultipleToolCalls = false,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
        };
        List<ChatMessage> messages = [new(ChatRole.User, question), .. fanOut.Messages];
        var pipeline = InvestigationPipeline.FunctionInvoking(model, _limits);
        var response = await pipeline.GetResponseAsync(messages, options, cancellationToken);
        if (turn.Accepted is null)
        {
            await pipeline.GetResponseAsync([.. messages, .. response.Messages], options, cancellationToken);
        }
    }

    private static Dictionary<string, object?> Args(IEnumerable<(string Name, string Value)> arguments) =>
        arguments.ToDictionary(a => a.Name, a => (object?)a.Value, StringComparer.Ordinal);

    // The processors a profile result routes the merchant to, read from the tool's own typed output.
    private static IEnumerable<string> RoutedProcessors(string profileResult)
    {
        using var json = JsonDocument.Parse(profileResult);
        return json.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("processors", out var routed)
            ? [.. routed.EnumerateArray().Select(p => p.GetProperty("id").GetString()!)]
            : [];
    }

    /// <summary>
    /// Runs planned calls through the guarded tools within the agent's call limit, keeping one call for knowledge,
    /// and keeps them as the assistant's calls and their results, in the order planned.
    /// </summary>
    private sealed class FanOut(IReadOnlyDictionary<string, AIFunction> readTools, int maxCalls)
    {
        private readonly List<FunctionCallContent> _calls = [];
        private readonly List<FunctionResultContent> _results = [];

        public IEnumerable<ChatMessage> Messages =>
            _calls.Count == 0 ? [] : [new ChatMessage(ChatRole.Assistant, [.. _calls]), new ChatMessage(ChatRole.Tool, [.. _results])];

        public async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<(string Tool, Dictionary<string, object?> Arguments)> planned, CancellationToken cancellationToken, bool last = false)
        {
            var room = maxCalls - _calls.Count - (last ? 0 : 1);
            var calls = planned.Where(p => readTools.ContainsKey(p.Tool)).Take(Math.Max(room, 0))
                .Select((p, i) => new FunctionCallContent($"f{_calls.Count + i + 1}", p.Tool, p.Arguments))
                .ToList();

            var results = await Task.WhenAll(calls.Select(async call =>
                (await readTools[call.Name].InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken))?.ToString() ?? string.Empty));
            _calls.AddRange(calls);
            _results.AddRange(calls.Zip(results, (call, result) => new FunctionResultContent(call.CallId, result)));
            return results;
        }
    }
}
