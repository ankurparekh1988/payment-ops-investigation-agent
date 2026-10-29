using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Domain.Security;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Wraps each read tool for one turn: enforces the call, repeat and correction limits and the per-tool
/// timeout, writes what the tool returned into the turn's state before the model sees it, and reports progress.
/// </summary>
internal sealed class ToolGuard(TurnState turn, InvestigationLimits limits, TimeProvider time, IUserContext user, IProgress<InvestigationProgress>? progress)
{
    private static readonly string LimitReached = ToolJson.Serialize(new ToolResult.Failed(
        ToolErrorCode.LimitReached, "The investigation has reached its step limit. Call submit_assessment with what you have."));

    private static readonly string AlreadySubmitted = ToolJson.Serialize(new ToolResult.Failed(
        ToolErrorCode.LimitReached, "The assessment has already been submitted."));

    // A request type's schema never changes, and a guard is built for every investigation.
    private static readonly ConcurrentDictionary<Type, JsonElement> Schemas = new();

    private readonly Lock _lock = new();
    private readonly Dictionary<string, int> _repeats = new(StringComparer.Ordinal);
    private int _calls;
    private int _corrections;

    public AIFunction Wrap(IInvestigationTool tool) => new GuardedFunction(this, tool);

    private async Task<string> InvokeAsync(IInvestigationTool tool, AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (turn.Accepted is not null)
        {
            ToolLoop.End();
            return AlreadySubmitted;
        }

        var request = Parse(tool, arguments);
        if (!Admit(tool, request))
        {
            Stop();
            return LimitReached;
        }

        var label = ProgressLabels.For(tool.Name, request);
        progress?.Report(new InvestigationProgress.ToolStarted(tool.Name, label));
        var result = request is not null
            ? await RunAsync(tool, request, cancellationToken)
            : new ToolResult.Failed(ToolErrorCode.InvalidArgument, "The arguments don't match this tool's parameters.");
        var (json, gap) = Record(tool, result);
        progress?.Report(new InvestigationProgress.ToolFinished(tool.Name, label, result is ToolResult.Ok, Records(result), gap?.Wording));
        if (result is ToolResult.Failed { Code: ToolErrorCode.InvalidArgument or ToolErrorCode.RangeTooLarge } && TooManyCorrections())
        {
            Stop();
        }

        return json;
    }

    private void Stop()
    {
        if (turn.Stop())
        {
            progress?.Report(new InvestigationProgress.LimitReached());
        }

        ToolLoop.End();
    }

    private static int Records(ToolResult result) => result switch
    {
        ToolResult.Ok { Body: KnowledgeSearchResult found } => found.Sections.Count,
        ToolResult.Ok ok => ok.Evidence.Count(e => e is not DerivedEvidence),
        _ => 0,
    };

    // Counts the call, and the request the tool would run if it parsed; false when this call would break a limit.
    private bool Admit(IInvestigationTool tool, object? request)
    {
        lock (_lock)
        {
            if (turn.Stopped || ++_calls > limits.MaxToolCalls)
            {
                return false;
            }

            if (request is null)
            {
                return true;
            }

            var key = $"{tool.Name}({Canonical(tool, request)})";
            _repeats[key] = _repeats.GetValueOrDefault(key) + 1;
            return _repeats[key] <= limits.MaxRepeats;
        }
    }

    private static object? Parse(IInvestigationTool tool, AIFunctionArguments arguments)
    {
        try
        {
            return JsonSerializer.SerializeToElement(arguments, ToolJson.Options).Deserialize(tool.RequestType, ToolJson.Requests);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ToolResult> RunAsync(IInvestigationTool tool, object request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(limits.ToolTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await tool.RunAsync(request, user, linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ToolResult.Failed(ToolErrorCode.Timeout, $"{tool.Source} didn't respond in time.", tool.Source);
        }
    }

    private (string Json, ObservedGap? Gap) Record(IInvestigationTool tool, ToolResult result)
    {
        if (result is ToolResult.Ok { Body: KnowledgeSearchResult found })
        {
            return (ToolJson.Serialize(new ToolResult.Ok(Numbered(found), [], [])), null);
        }

        var gap = result switch
        {
            ToolResult.Failed { Code: ToolErrorCode.Unavailable } failed => ObservedGap.Unavailable(failed.Source ?? tool.Source),
            ToolResult.Failed { Code: ToolErrorCode.Timeout } => ObservedGap.Timeout(tool.Source),
            ToolResult.Failed { Code: ToolErrorCode.NotAuthorized } => ObservedGap.NotAuthorized(tool.Name),
            _ => null,
        };
        if (gap is not null)
        {
            turn.AddGap(gap);
        }

        if (result is ToolResult.Ok ok)
        {
            turn.Register(ok.Evidence);
            foreach (var truncation in ok.Truncations)
            {
                turn.AddGap(ObservedGap.Truncated(truncation));
            }
        }

        // Knowledge failing is a gap, but only the data tools decide whether any structured source answered.
        if (tool.Source != DataSources.Knowledge && (result is ToolResult.Ok or ToolResult.Failed { Code: ToolErrorCode.NotFound } || gap?.Kind is ObservedGapKind.Unavailable or ObservedGapKind.Timeout))
        {
            turn.StructuredOutcome(answered: gap is null);
        }

        return (ToolJson.Serialize(result), gap);
    }

    private bool TooManyCorrections()
    {
        lock (_lock)
        {
            return ++_corrections > limits.MaxCorrections;
        }
    }

    // Citation ids are the turn's, assigned here; the content is shown as data, never as instructions.
    private object Numbered(KnowledgeSearchResult found) => new
    {
        Sections = found.Sections.Select(section => new
        {
            CitationId = turn.RegisterSection(section),
            Title = UntrustedText.Wrap(section.Title),
            SectionPath = UntrustedText.Wrap(section.SectionPath),
            Content = UntrustedText.Wrap(section.Content),
        }).ToList(),
    };

    // The parsed request with keys sorted, surrounding whitespace dropped and blanks left out, as tools treat them;
    // ids keep their case because they're matched exactly.
    private static string Canonical(IInvestigationTool tool, object request) =>
        string.Join(';', JsonSerializer.SerializeToElement(request, tool.RequestType, ToolJson.Options).EnumerateObject()
            .Select(p => (p.Name, Value: p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()!.Trim() : p.Value.GetRawText()))
            .Where(p => p.Value.Length > 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}"));

    /// <summary>A tool's parameter schema as the request carries it.</summary>
    public static JsonElement Schema(Type requestType) =>
        Schemas.GetOrAdd(requestType, type => AIJsonUtilities.CreateJsonSchema(type, serializerOptions: ToolJson.Requests));

    private sealed class GuardedFunction(ToolGuard guard, IInvestigationTool tool) : AIFunction
    {
        public override string Name => tool.Name;

        public override string Description => tool.Description;

        public override JsonElement JsonSchema { get; } = Schema(tool.RequestType);

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            await guard.InvokeAsync(tool, arguments, cancellationToken);
    }
}
