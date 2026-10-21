using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.AI;

using PaymentOps.Domain.Security;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Investigating;

/// <summary>
/// Wraps each read tool for one turn: enforces the call, repeat and correction limits and the per-tool
/// timeout, and writes what the tool returned into the turn's state before the model sees it.
/// </summary>
internal sealed class ToolGuard(TurnState turn, InvestigationLimits limits, TimeProvider time, IUserContext user)
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

        if (!Admit(tool.Name, arguments))
        {
            turn.Stop();
            ToolLoop.End();
            return LimitReached;
        }

        var result = Parse(tool, arguments) is { } request
            ? await RunAsync(tool, request, cancellationToken)
            : new ToolResult.Failed(ToolErrorCode.InvalidArgument, "The arguments don't match this tool's parameters.");
        return Record(tool, result);
    }

    // Counts the call and its canonical form; false when this call would break a limit.
    private bool Admit(string name, AIFunctionArguments arguments)
    {
        lock (_lock)
        {
            if (turn.Stopped || ++_calls > limits.MaxToolCalls)
            {
                return false;
            }

            var key = $"{name}({Canonical(arguments)})";
            _repeats[key] = _repeats.GetValueOrDefault(key) + 1;
            return _repeats[key] <= limits.MaxRepeats;
        }
    }

    private static object? Parse(IInvestigationTool tool, AIFunctionArguments arguments)
    {
        try
        {
            return JsonSerializer.SerializeToElement(arguments, ToolJson.Options).Deserialize(tool.RequestType, ToolJson.Options);
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

    private string Record(IInvestigationTool tool, ToolResult result)
    {
        if (result is ToolResult.Ok { Body: KnowledgeSearchResult found })
        {
            return ToolJson.Serialize(new ToolResult.Ok(Numbered(found), [], []));
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

        if (result is ToolResult.Failed { Code: ToolErrorCode.InvalidArgument or ToolErrorCode.RangeTooLarge } && TooManyCorrections())
        {
            turn.Stop();
            ToolLoop.End();
        }

        return ToolJson.Serialize(result);
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
            section.Title,
            section.SectionPath,
            Content = UntrustedText.Wrap(section.Content),
        }).ToList(),
    };

    // Keys sorted, surrounding whitespace dropped; ids keep their case because they're matched exactly.
    private static string Canonical(AIFunctionArguments arguments) =>
        string.Join(';', arguments.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={Text(a.Value)}"));

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => value.ToString()?.Trim() ?? string.Empty,
    };

    private sealed class GuardedFunction(ToolGuard guard, IInvestigationTool tool) : AIFunction
    {
        public override string Name => tool.Name;

        public override string Description => tool.Description;

        public override JsonElement JsonSchema { get; } = Schemas.GetOrAdd(tool.RequestType, type => AIJsonUtilities.CreateJsonSchema(type, serializerOptions: ToolJson.Options));

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            await guard.InvokeAsync(tool, arguments, cancellationToken);
    }
}
