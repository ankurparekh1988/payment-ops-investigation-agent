using System.Text.Json;
using System.Text.Json.Serialization;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Evaluation.Reporting;

internal sealed record PromptInfo(string Id, string Version);

internal sealed record ModelInfo(string Deployment, string Model, string Version, string DeploymentType)
{
    [JsonIgnore]
    public DeploymentIdentity Identity => new(Model, Version, DeploymentType, null);

    public static ModelInfo From(AiRelease release, string deployment)
    {
        ArgumentNullException.ThrowIfNull(release);
        var d = release.Deployments[deployment];
        return new ModelInfo(deployment, d.Model, d.Version, d.DeploymentType);
    }
}

/// <summary>What was evaluated: the system, the commit, its prompts and models, and when.</summary>
internal sealed record ReportContext(
    string System,
    string Commit,
    IReadOnlyList<PromptInfo> Prompts,
    ModelInfo? Chat,
    ModelInfo? Judge,
    string? JudgePromptVersion,
    DateTimeOffset StartedAt);

/// <param name="Judge">Case id to its judge score in this run; empty when the judge didn't run.</param>
internal sealed record RunReport(
    int Run,
    IReadOnlyList<CaseResult> Cases,
    IReadOnlyDictionary<string, JudgeScore> Judge,
    IReadOnlyDictionary<string, double?> Metrics,
    HardGates HardGates,
    int TicketsInStore);

/// <summary>One case across runs: its outcome in each, and whether each run met each metric it's eligible for.</summary>
internal sealed record CaseStability(string CaseId, string Group, string Set, IReadOnlyList<string> Outcomes, IReadOnlyDictionary<string, IReadOnlyList<bool>> Metrics);

/// <summary>Nearest-rank percentiles in milliseconds; null when nothing was measured.</summary>
internal sealed record Percentiles(long? P50, long? P95)
{
    public static Percentiles Of(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? new(null, null) : new(Rank(sorted, 0.50), Rank(sorted, 0.95));
    }

    private static long Rank(List<long> sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Count) - 1];
}

/// <summary>Estimated model cost, informational only. The chat cost is the system's own calls; the judge is separate.</summary>
internal sealed record CostReport(
    string? Currency,
    decimal? Chat,
    decimal? Judge,
    decimal? PerValidatedInvestigation,
    int ValidatedResults,
    TokenUse ChatTokens,
    TokenUse JudgeTokens,
    string? PriceSource);

internal sealed record GateReport(string Verdict, IReadOnlyList<string> Failures, string? ThresholdsFile, IReadOnlyDictionary<string, double> Thresholds)
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string ReportOnly = "report-only";
}

/// <summary>
/// One evaluation's record: every run's results without any model text, the metric summary and breakdowns, the
/// per-case stability view, latency, cost and the gate's verdict. Thresholds and the kill test are computed
/// from this file, so it reads back exactly as written.
/// </summary>
internal sealed record EvaluationReport(
    int SchemaVersion,
    string System,
    string Commit,
    IReadOnlyList<PromptInfo> Prompts,
    ModelInfo? Chat,
    ModelInfo? Judge,
    string? JudgePromptVersion,
    DateTimeOffset StartedAt,
    IReadOnlyList<RunReport> Runs,
    IReadOnlyDictionary<string, MetricSummary> Summary,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, MetricSummary>> Breakdown,
    IReadOnlyList<CaseStability> Stability,
    Percentiles Latency,
    Percentiles TimeToFirstProgress,
    CostReport Cost,
    GateReport Gate,
    IReadOnlyList<CaseRun> RetrievalCases)
{
    public const int SupportedSchemaVersion = 1;
    public const string AgentSystem = "agent";
    public const string FixedWorkflowSystem = "fixed-workflow";
    public const string RetrievalSystem = "retrieval";
    public const string RegressionSet = "regression";
    public const string HeldOutSet = "held-out";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>The gating metrics a run of these labels measures: those with any opportunities.</summary>
    public static IReadOnlySet<string> Applicable(IReadOnlyDictionary<string, int> n) =>
        ThresholdRule.Gating.Where(m => n.GetValueOrDefault(m) > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>The applicable gating metrics with their N, as the thresholds file must match them.</summary>
    public static IReadOnlyDictionary<string, int> ApplicableN(IReadOnlyDictionary<string, int> n) =>
        Applicable(n).ToDictionary(m => m, m => n[m], StringComparer.Ordinal);

    public static EvaluationReport ForInvestigations(
        ReportContext context,
        LoadedCases cases,
        IReadOnlyList<RunOutcome> runs,
        IReadOnlyList<IReadOnlyDictionary<string, JudgeScore>> judged,
        PriceList prices,
        Thresholds? thresholds,
        string? thresholdsFile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(prices);

        var metrics = runs.Select(run => Metrics.Score(cases, run)).ToList();
        var n = Opportunities.Count(cases.Cases);
        var summary = Aggregation.Summarize(metrics, n);
        var results = runs.SelectMany(run => run.Results).ToList();
        var judgeScores = judged.SelectMany(j => j.Values).ToList();

        var breakdown = new Dictionary<string, IReadOnlyDictionary<string, MetricSummary>>(StringComparer.Ordinal);
        foreach (var (name, filter) in Subsets(cases.Cases))
        {
            var subset = cases with { Cases = [.. cases.Cases.Where(filter)] };
            if (subset.Cases.Count > 0)
            {
                var ids = subset.Cases.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
                var subsetRuns = runs.Select(run => Metrics.Score(subset, new RunOutcome([.. run.Results.Where(r => ids.Contains(r.CaseId))], 0))).ToList();
                breakdown[name] = Aggregation.Summarize(subsetRuns, Opportunities.Count(subset.Cases));
            }
        }

        var chatTokens = results.Aggregate(TokenUse.Zero, (sum, r) => sum.Add(new TokenUse(r.InputTokens, r.CachedInputTokens, r.OutputTokens)));
        var judgeTokens = judgeScores.Aggregate(TokenUse.Zero, (sum, j) => sum.Add(new TokenUse(j.InputTokens, j.CachedInputTokens, j.OutputTokens)));
        var price = context.Chat is { } chat ? prices.Find(chat.Identity) : null;
        var chatCost = price is null ? null : (decimal?)PriceList.Cost(price, chatTokens);
        var validated = results.Count(r => InvestigationCase.AssessedOutcomes.Contains(r.Outcome));
        var cost = new CostReport(
            price?.Currency,
            chatCost,
            context.Judge is { } judge && judgeScores.Count > 0 ? prices.Cost(judge.Identity, judgeTokens) : null,
            PriceList.CostPerValidatedInvestigation(chatCost, validated),
            validated,
            chatTokens,
            judgeTokens,
            price?.Source);

        return new EvaluationReport(
            SupportedSchemaVersion,
            context.System,
            context.Commit,
            context.Prompts,
            context.Chat,
            context.Judge,
            context.JudgePromptVersion,
            context.StartedAt,
            [.. runs.Select((run, i) => new RunReport(i + 1, run.Results, judged[i], metrics[i].Values, metrics[i].Hard, run.TicketsInStore))],
            summary,
            breakdown,
            [.. cases.Cases.Select(c => StabilityOf(c, runs, metrics))],
            Percentiles.Of(results.Select(r => r.LatencyMs)),
            Percentiles.Of(results.Where(r => r.TtfpMs is not null).Select(r => r.TtfpMs!.Value)),
            cost,
            GateFor(metrics, summary, thresholds, thresholdsFile, Applicable(n)),
            []);
    }

    public static EvaluationReport ForRetrieval(ReportContext context, RetrievalResult result, Thresholds? thresholds, string? thresholdsFile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var n = RetrievalEval.Opportunities(result.Answerable);
        var summary = Aggregation.Summarize([result.Metrics], n);
        return new EvaluationReport(
            SupportedSchemaVersion,
            context.System,
            context.Commit,
            context.Prompts,
            context.Chat,
            context.Judge,
            context.JudgePromptVersion,
            context.StartedAt,
            [new RunReport(1, [], new Dictionary<string, JudgeScore>(), result.Metrics.Values, result.Metrics.Hard, 0)],
            summary,
            new Dictionary<string, IReadOnlyDictionary<string, MetricSummary>>(),
            [],
            Percentiles.Of([]),
            Percentiles.Of([]),
            new CostReport(null, null, null, null, 0, TokenUse.Zero, TokenUse.Zero, null),
            GateFor([result.Metrics], summary, thresholds, thresholdsFile, Applicable(n)),
            result.Cases);
    }

    public static EvaluationReport Load(string path)
    {
        var report = StrictFiles.Read<EvaluationReport>(path, Json);
        return report.SchemaVersion == SupportedSchemaVersion ? report
            : throw new InvalidDataException($"{path} is report schema version {report.SchemaVersion}; this build reads {SupportedSchemaVersion}.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    private static GateReport GateFor(IReadOnlyList<RunMetrics> runs, IReadOnlyDictionary<string, MetricSummary> summary, Thresholds? thresholds, string? file, IReadOnlySet<string> applicable)
    {
        var verdict = Scoring.Gate.Evaluate(runs, summary, thresholds, applicable);
        var shown = thresholds?.Metrics.Where(m => applicable.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value.Threshold, StringComparer.Ordinal)
            ?? new Dictionary<string, double>(StringComparer.Ordinal);
        var outcome = !verdict.Passed ? GateReport.Failed : verdict.ReportOnly ? GateReport.ReportOnly : GateReport.Passed;
        return new GateReport(outcome, verdict.Failures, file, shown);
    }

    // Regression and held-out are always shown apart, and each group on its own.
    private static IEnumerable<(string Name, Func<InvestigationCase, bool> Filter)> Subsets(IReadOnlyList<InvestigationCase> cases) =>
    [
        (RegressionSet, c => c.Group == InvestigationCase.RegressionGroup),
        (HeldOutSet, c => c.Group != InvestigationCase.RegressionGroup),
        .. cases.Select(c => c.Group).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(group => ($"group:{group}", (Func<InvestigationCase, bool>)(c => c.Group == group))),
    ];

    private static CaseStability StabilityOf(InvestigationCase c, IReadOnlyList<RunOutcome> runs, List<RunMetrics> metrics)
    {
        var perRun = metrics.Select(m => m.PerCase.GetValueOrDefault(c.Id) ?? new Dictionary<string, bool>()).ToList();
        var names = perRun.SelectMany(p => p.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return new CaseStability(
            c.Id,
            c.Group,
            c.Group == InvestigationCase.RegressionGroup ? RegressionSet : HeldOutSet,
            [.. runs.Select(run => run.Results.FirstOrDefault(r => r.CaseId == c.Id)?.Outcome ?? Outcomes.Missing)],
            names.ToDictionary(name => name, name => (IReadOnlyList<bool>)[.. perRun.Select(p => p.GetValueOrDefault(name))], StringComparer.Ordinal));
    }
}
