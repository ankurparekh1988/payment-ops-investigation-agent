using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Safety;

namespace PaymentOps.Evaluation.Running;

/// <summary>Requests one screening check made, each attempt counted, and how they ended. A request can be in one failure class at most.</summary>
/// <param name="Retries">The Content Safety client's own retries. Always 0 on a classifier, whose retries belong to the model SDK and show only as more requests.</param>
/// <param name="OtherFailures">Any other refused request or failed connection. On a classifier this includes a call the content filter refused, which is a block, not an outage.</param>
internal sealed record RequestCounts(int Requests, int Retries, int Throttled, int ServerErrors, int TimedOut, int OtherFailures);

/// <summary>Screens run and how many couldn't answer.</summary>
/// <param name="OutOfTime">Of those that couldn't answer, how many ran out of their time budget.</param>
/// <param name="Unexplained">Of those that couldn't answer, how many had enough time and no failed request: a malformed answer, a refused sign-in or no endpoint.</param>
internal sealed record ScreenCounts(int Screens, int Unavailable, int OutOfTime, int Unexplained);

/// <summary>A case attempt the environment spoiled, and which checks failed while it ran.</summary>
internal sealed record CaseRetry(int Run, string CaseId, string Cause, IReadOnlyList<string> Seen);

/// <summary>Counts and case ids only. Never a question, tool text, prompt, endpoint, token or service answer.</summary>
internal sealed record ScreeningDiagnostics(
    RequestCounts PromptShields,
    RequestCounts HarmAnalysis,
    RequestCounts QuestionClassifier,
    RequestCounts ToolTextClassifier,
    ScreenCounts QuestionScreen,
    ScreenCounts ToolTextScreen,
    int SignInFailures,
    IReadOnlyList<CaseRetry> CaseRetries);

/// <summary>The counts at one moment; only <see cref="EvaluationDiagnostics"/> reads them.</summary>
internal sealed record CountsMark(IReadOnlyDictionary<(string Component, string Kind), int> Counts);

/// <summary>
/// Why screening couldn't answer, seen from outside the app: the screens are wrapped and each HTTP request is read from
/// the runtime's own diagnostics, so the app's code is what ships. Cases run one at a time, so the failures counted
/// during an attempt are that attempt's.
/// </summary>
internal sealed class EvaluationDiagnostics
{
    private const string PromptShields = "Prompt Shields";
    private const string HarmAnalysis = "Content Safety harm analysis";
    private const string QuestionClassifier = "question classifier";
    private const string ToolTextClassifier = "tool-text classifier";
    private const string QuestionScreen = "question screen";
    private const string ToolTextScreen = "tool-text screen";
    private const string SignIn = "sign-in";

    private const string Requests = "request";
    private const string Retries = "retry";
    private const string Throttled = "throttled";
    private const string ServerError = "server error";
    private const string TimedOut = "timed out";
    private const string OtherFailure = "other failure";
    private const string Screens = "screen";
    private const string Unavailable = "couldn't answer";
    private const string OutOfTime = "out of time";
    private const string Unexplained = "unexplained";
    private const string Failure = "failure";

    private static readonly string[] Checks = [PromptShields, HarmAnalysis, QuestionClassifier, ToolTextClassifier, QuestionScreen, ToolTextScreen, SignIn];
    private static readonly string[] FailureKinds = [Throttled, ServerError, TimedOut, OtherFailure, OutOfTime, Unexplained, Failure];

    // A deadline's timer can fire slightly before a stopwatch reaches the same budget.
    private static readonly TimeSpan DeadlineTolerance = TimeSpan.FromMilliseconds(100);

    // The screen a request belongs to flows with the call, so concurrent screens stay apart.
    private readonly AsyncLocal<ScreenScope?> _scope = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Component, string Kind), int> _counts = [];
    private readonly List<CaseRetry> _caseRetries = [];

    // The Content Safety client resends the same body object on its one retry; weak, so no body outlives its request.
    private readonly ConditionalWeakTable<object, object> _bodiesSent = [];

    /// <summary>Starts reading requests from the runtime's diagnostics, until disposed.</summary>
    public IDisposable Listen() => new RequestListener(this);

    public IUntrustedTextScreen Observe(IUntrustedTextScreen screen) => new ObservedToolTextScreen(screen, this);

    /// <param name="budget">The question screen's own time limit; it hides its deadline, so the time it took is what shows it.</param>
    public IQuestionScreen Observe(IQuestionScreen screen, TimeSpan budget) => new ObservedQuestionScreen(screen, budget, this);

    /// <summary>The app's configuration, then its screens wrapped where they're registered.</summary>
    public Action<IServiceCollection> Observing(Action<IServiceCollection> appConfiguration) => services =>
    {
        appConfiguration(services);
        EvaluationServices.Wrap<IUntrustedTextScreen>(services, (_, screen) => Observe(screen));
        EvaluationServices.Wrap<IQuestionScreen>(services, (sp, screen) => Observe(screen, sp.GetRequiredService<IOptions<InvestigationLimits>>().Value.QuestionScreenTimeout));
    };

    public void SignInFailed() => Add(SignIn, Failure);

    public void CaseRetried(int run, string caseId, string cause, IReadOnlyList<string> seen)
    {
        lock (_lock)
        {
            _caseRetries.Add(new CaseRetry(run, caseId, cause, seen));
        }
    }

    /// <summary>Where the counts stand, so the failures of what happens next can be told apart.</summary>
    public CountsMark Mark()
    {
        lock (_lock)
        {
            return new CountsMark(new Dictionary<(string Component, string Kind), int>(_counts));
        }
    }

    /// <summary>Each check with its failures since the mark, such as <c>tool-text classifier: 2 throttled</c>.</summary>
    public IReadOnlyList<string> FailuresSince(CountsMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);

        lock (_lock)
        {
            var lines = new List<string>();
            foreach (var check in Checks)
            {
                var failures = FailureKinds
                    .Select(kind => Described(kind, _counts.GetValueOrDefault((check, kind)) - mark.Counts.GetValueOrDefault((check, kind))))
                    .OfType<string>()
                    .ToList();
                if (failures.Count > 0)
                {
                    lines.Add($"{check}: {string.Join(", ", failures)}");
                }
            }

            return lines;
        }
    }

    /// <summary>One line for an annotation: everything that failed in the whole evaluation.</summary>
    public string Describe() =>
        FailuresSince(new CountsMark(new Dictionary<(string Component, string Kind), int>())) is { Count: > 0 } failures ? string.Join("; ", failures) : "no screening failure was seen";

    private static string? Described(string kind, int count) =>
        count <= 0 ? null : $"{count} {kind}{(count > 1 && kind is ServerError or OtherFailure or Failure ? "s" : string.Empty)}";

    public ScreeningDiagnostics Snapshot()
    {
        lock (_lock)
        {
            return new ScreeningDiagnostics(
                RequestsOf(PromptShields),
                RequestsOf(HarmAnalysis),
                RequestsOf(QuestionClassifier),
                RequestsOf(ToolTextClassifier),
                ScreensOf(QuestionScreen),
                ScreensOf(ToolTextScreen),
                _counts.GetValueOrDefault((SignIn, Failure)),
                [.. _caseRetries]);
        }
    }

    private RequestCounts RequestsOf(string component) => new(
        _counts.GetValueOrDefault((component, Requests)),
        _counts.GetValueOrDefault((component, Retries)),
        _counts.GetValueOrDefault((component, Throttled)),
        _counts.GetValueOrDefault((component, ServerError)),
        _counts.GetValueOrDefault((component, TimedOut)),
        _counts.GetValueOrDefault((component, OtherFailure)));

    private ScreenCounts ScreensOf(string screen) => new(
        _counts.GetValueOrDefault((screen, Screens)),
        _counts.GetValueOrDefault((screen, Unavailable)),
        _counts.GetValueOrDefault((screen, OutOfTime)),
        _counts.GetValueOrDefault((screen, Unexplained)));

    private void Add(string component, string kind)
    {
        lock (_lock)
        {
            _counts[(component, kind)] = _counts.GetValueOrDefault((component, kind)) + 1;
        }
    }

    private void Screened(string screen, ScreenScope scope, bool unavailable, bool outOfTime)
    {
        Add(screen, Screens);
        if (unavailable)
        {
            Add(screen, Unavailable);
        }

        if (outOfTime)
        {
            Add(screen, OutOfTime);
        }
        else if (unavailable && !scope.RequestFailed)
        {
            Add(screen, Unexplained);
        }
    }

    // Only a screen's own requests count: the agent's model calls run outside any screen.
    private void RequestEnded(HttpRequestMessage? request, HttpResponseMessage? response, TaskStatus status)
    {
        if (_scope.Value is not { } scope || request?.RequestUri is not { } uri || Component(uri.AbsolutePath, scope.Classifier) is not { } component)
        {
            return;
        }

        Add(component, Requests);
        if (request.Content is JsonContent { Value: { } body } && !_bodiesSent.TryAdd(body, body))
        {
            Add(component, Retries);
        }

        if (FailureClass(response, status) is { } failure)
        {
            scope.RequestFailed = true;
            Add(component, failure);
        }
    }

    private static string? Component(string path, string classifier) =>
        path.EndsWith("text:shieldPrompt", StringComparison.Ordinal) ? PromptShields
        : path.EndsWith("text:analyze", StringComparison.Ordinal) ? HarmAnalysis
        : path.EndsWith("chat/completions", StringComparison.Ordinal) ? classifier
        : null;

    private static string? FailureClass(HttpResponseMessage? response, TaskStatus status) => (response, status) switch
    {
        (_, TaskStatus.Canceled) => TimedOut,
        (null, _) => OtherFailure,
        ({ IsSuccessStatusCode: true }, _) => null,
        ({ StatusCode: HttpStatusCode.TooManyRequests }, _) => Throttled,
        ({ StatusCode: >= HttpStatusCode.InternalServerError }, _) => ServerError,
        _ => OtherFailure,
    };

    private sealed class ScreenScope(string classifier)
    {
        public string Classifier { get; } = classifier;

        public bool RequestFailed { get; set; }
    }

    private sealed class ObservedToolTextScreen(IUntrustedTextScreen inner, EvaluationDiagnostics owner) : IUntrustedTextScreen
    {
        public async Task<IReadOnlyList<ScreenVerdict>> ScreenAsync(IReadOnlyList<string> segments, CancellationToken cancellationToken)
        {
            var scope = owner._scope.Value = new ScreenScope(ToolTextClassifier);
            try
            {
                var verdicts = await inner.ScreenAsync(segments, cancellationToken);
                owner.Screened(ToolTextScreen, scope, verdicts.Contains(ScreenVerdict.Unavailable), outOfTime: false);
                return verdicts;
            }
            catch (OperationCanceledException)
            {
                owner.Screened(ToolTextScreen, scope, unavailable: true, outOfTime: true);
                throw;
            }
        }
    }

    private sealed class ObservedQuestionScreen(IQuestionScreen inner, TimeSpan budget, EvaluationDiagnostics owner) : IQuestionScreen
    {
        public async Task<QuestionVerdict> ScreenAsync(string question, CancellationToken cancellationToken)
        {
            var scope = owner._scope.Value = new ScreenScope(QuestionClassifier);
            var timer = Stopwatch.StartNew();
            var verdict = await inner.ScreenAsync(question, cancellationToken);
            var unavailable = verdict == QuestionVerdict.Unavailable;
            owner.Screened(QuestionScreen, scope, unavailable, outOfTime: unavailable && timer.Elapsed >= budget - DeadlineTolerance);
            return verdict;
        }
    }

    /// <summary>Follows the runtime's HTTP diagnostics; the payload types are internal to it, so they're read by name.</summary>
    private sealed class RequestListener : IDisposable, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private const string HttpListenerName = "HttpHandlerDiagnosticListener";
        private const string RequestEvents = "System.Net.Http.HttpRequestOut";
        private const string RequestEnd = RequestEvents + ".Stop";

        private readonly EvaluationDiagnostics _owner;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _listeners;
        private bool _stopped;

        public RequestListener(EvaluationDiagnostics owner)
        {
            _owner = owner;
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void Dispose()
        {
            _listeners.Dispose();
            lock (_subscriptions)
            {
                _stopped = true;
                _subscriptions.ForEach(s => s.Dispose());
            }
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name != HttpListenerName)
            {
                return;
            }

            lock (_subscriptions)
            {
                if (!_stopped)
                {
                    _subscriptions.Add(value.Subscribe(this, (name, _, _) => name.StartsWith(RequestEvents, StringComparison.Ordinal)));
                }
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RequestEnd && value.Value is { } payload)
            {
                var type = payload.GetType();
                _owner.RequestEnded(
                    type.GetProperty("Request")?.GetValue(payload) as HttpRequestMessage,
                    type.GetProperty("Response")?.GetValue(payload) as HttpResponseMessage,
                    type.GetProperty("RequestTaskStatus")?.GetValue(payload) is TaskStatus status ? status : TaskStatus.RanToCompletion);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
