using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Investigating;

/// <summary>A model call the investigation's budget doesn't admit; the attempt stops at its limit.</summary>
internal sealed class InvestigationBudgetExceededException(string bound)
    : Exception($"The investigation has no {bound} budget left for another model call.")
{
    /// <summary><c>model_calls</c>, <c>tokens</c> or <c>estimate_unavailable</c>.</summary>
    public string Bound { get; } = bound;
}

/// <summary>What one admitted model call was estimated to use, so its charge can follow when it returns.</summary>
internal readonly record struct Admission(int EstimatedInput, int MaxOutput);

/// <summary>
/// Everything one investigation may use, shared by every attempt and never renewed (ADR 0023): one deadline, the read-tool
/// calls, the logical model calls, and an estimated token allowance checked before each call. The allowance is not an
/// absolute ceiling: the service's own framing can differ from the estimate, and billing follows what the service reports.
/// </summary>
internal sealed class InvestigationBudget : IDisposable
{
    private readonly InvestigationLimits _limits;
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly CancellationTokenSource _deadline;
    private readonly Lock _lock = new();
    private int _toolCalls;
    private int _logicalCalls;
    private int _estimateExceeded;
    private long _tokensUsed;

    // Within the current attempt: the last call's measured (or charged) input and the messages it sent.
    private long? _previousInput;
    private int _previousMessages;

    public InvestigationBudget(InvestigationLimits limits, TimeProvider time)
    {
        _limits = limits;
        _time = time;
        _started = time.GetTimestamp();
        _deadline = new CancellationTokenSource(limits.TurnTimeout, time);
    }

    public CancellationToken Deadline => _deadline.Token;

    public bool DeadlineReached => _deadline.IsCancellationRequested;

    public TimeSpan Remaining
    {
        get
        {
            var left = _limits.TurnTimeout - _time.GetElapsedTime(_started);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public int ToolCalls => Read(() => _toolCalls);

    public int LogicalCalls => Read(() => _logicalCalls);

    public long TokensUsed => Read(() => _tokensUsed);

    /// <summary>Calls whose measured input exceeded their estimate: evidence the estimate bounds what it should.</summary>
    public int EstimateExceeded => Read(() => _estimateExceeded);

    /// <summary>The input estimate of the last admitted call.</summary>
    public int LastEstimate { get; private set; }

    /// <summary>A new attempt sends a new conversation, so its first call is estimated from the request itself.</summary>
    public void StartAttempt()
    {
        lock (_lock)
        {
            _previousInput = null;
            _previousMessages = 0;
        }
    }

    public bool TryTakeToolCall()
    {
        lock (_lock)
        {
            if (_toolCalls >= _limits.MaxToolCalls)
            {
                return false;
            }

            _toolCalls++;
            return true;
        }
    }

    /// <summary>Admits one logical model call, or throws when it would break a bound; nothing is sent then.</summary>
    public Admission Admit(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        lock (_lock)
        {
            if (_logicalCalls >= _limits.MaxModelRequests)
            {
                throw new InvestigationBudgetExceededException("model_calls");
            }

            // A later call re-sends what the last one sent plus what was added since; anything else is estimated whole.
            var estimate = _previousInput is { } previous && messages.Count >= _previousMessages
                ? RequestSizeEstimate.Appended(messages.Skip(_previousMessages)) is { } added ? (int)Math.Min(previous + added, int.MaxValue) : (int?)null
                : RequestSizeEstimate.FirstCall(messages, options);
            if (estimate is not { } input)
            {
                throw new InvestigationBudgetExceededException("estimate_unavailable");
            }

            var output = options?.MaxOutputTokens ?? _limits.MaxOutputTokens;
            if (_tokensUsed + input + output > _limits.MaxInvestigationTokens)
            {
                throw new InvestigationBudgetExceededException("tokens");
            }

            _logicalCalls++;
            _previousMessages = messages.Count;
            LastEstimate = input;
            return new Admission(input, output);
        }
    }

    /// <summary>Charges a returned call what it reported, or its projection when it reported nothing.</summary>
    public void Charge(Admission admission, UsageDetails? usage)
    {
        lock (_lock)
        {
            if (usage is { InputTokenCount: { } input, OutputTokenCount: { } output })
            {
                _tokensUsed += input + output;
                _previousInput = input;
                if (input > admission.EstimatedInput)
                {
                    _estimateExceeded++;
                    Telemetry.TurnUsage.RecordEstimateExceeded();
                }
            }
            else
            {
                _tokensUsed += admission.EstimatedInput + admission.MaxOutput;
                _previousInput = admission.EstimatedInput;
            }
        }
    }

    /// <summary>Why a second attempt can't start within what's left, or null when it can.</summary>
    /// <param name="firstCallEstimate">The escalated attempt's first request, estimated before it's sent; null if it can't be.</param>
    public string? RefuseEscalation(int? firstCallEstimate)
    {
        lock (_lock)
        {
            if (Remaining < _limits.MinEscalationTime)
            {
                return "time";
            }

            if (_limits.MaxToolCalls - _toolCalls < _limits.MinEscalationToolCalls)
            {
                return "tool_calls";
            }

            if (_limits.MaxModelRequests - _logicalCalls < _limits.MinEscalationModelRequests)
            {
                return "model_calls";
            }

            if (_limits.MaxInvestigationTokens - _tokensUsed < _limits.MinEscalationTokens)
            {
                return "tokens";
            }

            if (firstCallEstimate is not { } estimate)
            {
                return "estimate_unavailable";
            }

            return _tokensUsed + estimate + _limits.MaxOutputTokens > _limits.MaxInvestigationTokens ? "first_call" : null;
        }
    }

    public void Dispose() => _deadline.Dispose();

    private T Read<T>(Func<T> value)
    {
        lock (_lock)
        {
            return value();
        }
    }
}
