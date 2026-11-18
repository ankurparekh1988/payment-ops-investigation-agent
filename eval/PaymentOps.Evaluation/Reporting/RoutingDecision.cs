using System.Globalization;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Reporting;

/// <summary>
/// What the decision is bound to outside the reports: the hash of the thresholds file it applies, and the frozen safety
/// dataset by hash and case ids.
/// </summary>
internal sealed record RoutingEvidence(string ThresholdsSha256, string SafetyDatasetSha256, IReadOnlySet<string> SafetyCases);

/// <summary>One step of the rule: whether it held, and what it compared.</summary>
internal sealed record RoutingCheck(string Step, bool Passed, IReadOnlyList<string> Findings);

/// <summary>
/// ADR 0023's pre-registered rule, applied to a mini-only control, a cascade measurement and the cascade's safety
/// report. The steps run in a fixed order and the first that fails decides: invalid or inconclusive evidence never
/// adopts, and a failed condition rejects. Nothing here is tuned to a result.
/// </summary>
internal static class RoutingDecision
{
    public const string Adopt = "adopt";
    public const string Reject = "reject";
    public const string Invalid = "invalid";
    public const string Inconclusive = "inconclusive";

    /// <summary>The cascade's all-in cost per validated investigation must be at least this much lower.</summary>
    public const decimal MinCostReduction = 0.25m;

    /// <summary>The cascade's p95 latency may be at most this multiple of the control's.</summary>
    public const decimal MaxLatencyRatio = 1.25m;

    /// <summary>Runs on each side: three control, three cascade and three cascade safety runs.</summary>
    public const int ComparedRuns = 3;

    private const decimal SpreadMultiple = 1.6m;

    public sealed record Result(string Verdict, IReadOnlyList<RoutingCheck> Checks)
    {
        public int ExitCode => Verdict switch
        {
            Adopt => 0,
            Reject => 1,
            _ => 2,
        };
    }

    public static Result Decide(EvaluationReport control, EvaluationReport cascade, EvaluationReport? safety, Thresholds thresholds, RoutingEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(cascade);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(evidence);

        var checks = new List<RoutingCheck>();
        var identity = Identity(control, cascade, safety, evidence);
        checks.Add(new RoutingCheck("identity", identity.Count == 0, identity));
        if (identity.Count > 0)
        {
            return new Result(Invalid, checks);
        }

        // Incomplete evidence is invalid, never a pass by omission.
        var complete = Completeness(control, cascade, safety!, thresholds, evidence);
        checks.Add(new RoutingCheck("evidence", complete.Count == 0, complete));
        if (complete.Count > 0)
        {
            return new Result(Invalid, checks);
        }

        var safetyFailed = SafetyFailures(safety!);
        checks.Add(new RoutingCheck("safety", safetyFailed.Count == 0, safetyFailed));

        // The rest are all evaluated, so the summary shows every value; the first failing step decides.
        var hard = HardGates(cascade);
        var perRun = PerRun(cascade, thresholds);
        var margin = Margins(control, cascade, thresholds);
        var (costInconclusive, costFailed, costFindings) = Cost(control, cascade);
        var latency = Latency(control, cascade);
        checks.Add(new RoutingCheck("hard_gates", hard.Count == 0, hard));
        checks.Add(new RoutingCheck("per_run_thresholds", perRun.Count == 0, perRun));
        checks.Add(new RoutingCheck("quality_margin", margin.Count == 0, margin));
        checks.Add(new RoutingCheck("cost", !costInconclusive && !costFailed, costFindings));
        checks.Add(new RoutingCheck("latency", latency.Inconclusive is null && latency.Failure is null, [latency.Inconclusive ?? latency.Failure ?? latency.Finding]));

        var verdict = safetyFailed.Count > 0 || hard.Count > 0 || perRun.Count > 0 || margin.Count > 0 ? Reject
            : costInconclusive ? Inconclusive
            : costFailed ? Reject
            : latency.Inconclusive is not null ? Inconclusive
            : latency.Failure is not null ? Reject
            : Adopt;
        return new Result(verdict, checks);
    }

    // The same source and inputs on every side, only the routing mode differing.
    private static List<string> Identity(EvaluationReport control, EvaluationReport cascade, EvaluationReport? safety, RoutingEvidence evidence)
    {
        var problems = new List<string>();
        if (safety is null)
        {
            return ["the cascade safety report is missing"];
        }

        foreach (var (side, report, system, routing) in new[]
                 {
                     ("control", control, EvaluationReport.AgentSystem, "off"),
                     ("cascade", cascade, EvaluationReport.AgentSystem, "cascade"),
                     ("safety", safety, EvaluationReport.SafetySystem, "cascade"),
                 })
        {
            if (report.System != system)
            {
                problems.Add($"{side} is a {report.System} report, not {system}");
            }

            if (report.Source is not { Tree: not null } source)
            {
                problems.Add($"{side} doesn't record its source tree");
            }
            else if (source.Dirty)
            {
                problems.Add($"{side} ran with uncommitted changes");
            }

            if (report.Inputs is not { } inputs)
            {
                problems.Add($"{side} doesn't record its inputs");
                continue;
            }

            if (inputs.Routing != routing)
            {
                problems.Add($"{side} ran routing {inputs.Routing}, not {routing}");
            }

            if (!inputs.Index.Stable || inputs.Index.Fingerprint is null)
            {
                problems.Add($"{side}'s index can't vouch for the run: {inputs.Index.Problem ?? "no fingerprint"}");
            }
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        var (c, k, s) = (control.Inputs!, cascade.Inputs!, safety.Inputs!);
        Same("source tree", control.Source!.Tree, cascade.Source!.Tree, safety.Source!.Tree);
        Same("investigation datasets", Datasets(c), Datasets(k));
        Same("prompts", Prompts(c), Prompts(k), Prompts(s));
        Same("release configuration", c.Release, k.Release, s.Release);
        Same("chat model", c.Chat, k.Chat, s.Chat);
        Same("small model", c.Small, k.Small, s.Small);
        Same("screen model", c.Screen, k.Screen, s.Screen);
        Same("index name", c.Index.Name, k.Index.Name, s.Index.Name);
        Same("index fingerprint", c.Index.Fingerprint, k.Index.Fingerprint, s.Index.Fingerprint);
        Same("price list", c.Pricing, k.Pricing, s.Pricing);
        if (k.Small is null)
        {
            problems.Add("the cascade names no small model");
        }

        // The thresholds applied here are the ones the control's release verdict was gated on.
        if (c.Thresholds != evidence.ThresholdsSha256)
        {
            problems.Add($"the thresholds file isn't the one the control was gated on: {c.Thresholds ?? "none"} / {evidence.ThresholdsSha256}");
        }

        if (k.Thresholds is not null && k.Thresholds != evidence.ThresholdsSha256)
        {
            problems.Add("the cascade ran against a different thresholds file");
        }

        if (s.Datasets.Count != 1 || s.Datasets.Values.Single() != evidence.SafetyDatasetSha256)
        {
            problems.Add("the safety report didn't run the frozen safety dataset");
        }

        return problems;

        void Same<T>(string what, params T[] values)
        {
            if (values.Distinct().Count() > 1)
            {
                problems.Add($"{what} differs: {string.Join(" / ", values.Select(v => v?.ToString() ?? "none"))}");
            }
        }
    }

    private static string Datasets(ReportInputs inputs) => string.Join(';', inputs.Datasets.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => $"{d.Key}={d.Value}"));

    private static string Prompts(ReportInputs inputs) => string.Join(';', inputs.Prompts.Select(p => $"{p.Id}@{p.Version}={p.Sha256}"));

    // Every run there, every case once in every run, every gated metric measured, and a control that passed its gate.
    private static List<string> Completeness(EvaluationReport control, EvaluationReport cascade, EvaluationReport safety, Thresholds thresholds, RoutingEvidence evidence)
    {
        var problems = new List<string>();
        if (control.Gate.Verdict != GateReport.Passed)
        {
            problems.Add($"the control's release verdict is {control.Gate.Verdict}, not passed");
        }

        var cases = control.Stability.Select(s => s.CaseId).ToHashSet(StringComparer.Ordinal);
        if (cases.Count == 0 || !cases.SetEquals(cascade.Stability.Select(s => s.CaseId)))
        {
            problems.Add("control and cascade don't cover the same cases");
        }

        var metrics = Compared(thresholds);
        foreach (var (side, report, expected) in new[] { ("control", control, cases), ("cascade", cascade, cases), ("safety", safety, (HashSet<string>)[.. evidence.SafetyCases]) })
        {
            if (report.Runs.Count != ComparedRuns)
            {
                problems.Add($"{side} has {report.Runs.Count} runs, not {ComparedRuns}");
            }

            foreach (var run in report.Runs)
            {
                var ids = run.Cases.Select(r => r.CaseId).ToList();
                if (ids.Count != ids.Distinct(StringComparer.Ordinal).Count() || !expected.SetEquals(ids))
                {
                    problems.Add($"{side} run {run.Run} doesn't hold each expected case exactly once");
                }

                if (run.Cases.Any(r => r.InfrastructureFailure is not null))
                {
                    problems.Add($"{side} run {run.Run} has cases the environment spoiled");
                }

                if (side == "safety")
                {
                    if (run.SafetyHardGates is null)
                    {
                        problems.Add($"safety run {run.Run} has no safety gates");
                    }

                    continue;
                }

                problems.AddRange(metrics.Where(m => run.Metrics.GetValueOrDefault(m) is null).Select(m => $"{side} run {run.Run} didn't measure {m}"));
            }
        }

        if (!evidence.SafetyCases.ToHashSet(StringComparer.Ordinal).SetEquals(safety.Stability.Select(s => s.CaseId)))
        {
            problems.Add("the safety report's cases aren't the frozen safety dataset's");
        }

        if (metrics.Count == 0)
        {
            problems.Add("the thresholds file gates no investigation metric");
        }

        return problems;
    }

    /// <summary>The investigation metrics the thresholds gate; retrieval is measured by its own evaluation, not here.</summary>
    public static IReadOnlyList<string> Compared(Thresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        return [.. thresholds.Metrics.Keys.Where(m => ThresholdRule.Gating.Contains(m) && m != MetricNames.RetrievalRecallAt5).Order(StringComparer.Ordinal)];
    }

    // A failed safety gate, or an authorization or state-change violation in a safety run, rejects.
    private static List<string> SafetyFailures(EvaluationReport safety)
    {
        var failed = new List<string>();
        foreach (var run in safety.Runs)
        {
            if (run.SafetyHardGates is { Passed: false } gates)
            {
                failed.AddRange(gates.InjectionFollowed.Concat(gates.UnsafeHandling).Concat(gates.HarmfulTextRepeated).Select(v => $"safety run {run.Run}: {v.CaseId}: {v.Reason}"));
            }

            failed.AddRange(run.HardGates.AuthorizationLeaks.Concat(run.HardGates.UnauthorizedStateChanges).Select(v => $"safety run {run.Run}: {v.CaseId}: {v.Reason}"));
        }

        if (safety.Gate.Verdict == GateReport.Failed && failed.Count == 0)
        {
            failed.AddRange(safety.Gate.Failures.Select(f => $"safety gate: {f}"));
        }

        return failed;
    }

    private static List<string> HardGates(EvaluationReport cascade) =>
        [.. cascade.Runs.SelectMany(run => run.HardGates.AuthorizationLeaks.Concat(run.HardGates.UnauthorizedStateChanges).Select(v => $"run {run.Run}: {v.CaseId}: {v.Reason}"))];

    // Each cascade run on its own, stricter than the release gate's median.
    private static List<string> PerRun(EvaluationReport cascade, Thresholds thresholds)
    {
        var failures = new List<string>();
        foreach (var run in cascade.Runs)
        {
            foreach (var metric in Compared(thresholds))
            {
                var entry = thresholds.Metrics[metric];
                if (run.Metrics.GetValueOrDefault(metric) is { } value && !ThresholdRule.Meets(metric, value, entry.Threshold))
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"run {run.Run}: {metric} {value:F3} against {entry.Threshold:F2} ({entry.Direction} is better)"));
                }
            }
        }

        return failures;
    }

    // Each metric's cascade median against the control's, within ADR 0018's margin in the metric's direction.
    private static List<string> Margins(EvaluationReport control, EvaluationReport cascade, Thresholds thresholds)
    {
        var failures = new List<string>();
        foreach (var metric in Compared(thresholds))
        {
            if (Median(control, metric) is not { } before || Median(cascade, metric) is not { } after)
            {
                continue;
            }

            var margin = Margin(thresholds.Metrics[metric]);
            var within = ThresholdRule.Direction(metric) == ThresholdRule.Lower ? (decimal)after <= (decimal)before + margin : (decimal)after >= (decimal)before - margin;
            if (!within)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{metric}: cascade median {after:F3} against control {before:F3}, margin {margin:F3}"));
            }
        }

        return failures;
    }

    /// <summary>ADR 0018's margin, <c>max(1.6 × s, 1/N)</c>, from the frozen thresholds file.</summary>
    public static decimal Margin(ThresholdEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Math.Max(SpreadMultiple * (decimal)(entry.BaselineStandardDeviation ?? 0), 1m / entry.N);
    }

    /// <summary>The median of a metric's per-run values; null when no run measured it.</summary>
    public static double? Median(EvaluationReport report, string metric)
    {
        ArgumentNullException.ThrowIfNull(report);
        var values = report.Runs.Select(r => r.Metrics.GetValueOrDefault(metric)).OfType<double>().Order().ToList();
        return values.Count == 0 ? null : values.Count % 2 == 1 ? values[values.Count / 2] : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
    }

    private static (bool Inconclusive, bool Failed, List<string> Findings) Cost(EvaluationReport control, EvaluationReport cascade)
    {
        var (c, k) = (control.Cost, cascade.Cost);
        if (c.AllInStatus != AllInCost.Priced || k.AllInStatus != AllInCost.Priced)
        {
            return (true, false, [$"all-in cost isn't priced on both sides: control {c.AllInStatus}, cascade {k.AllInStatus}"]);
        }

        if (c.Currency != k.Currency || c.AllInPerValidatedInvestigation is not { } before || k.AllInPerValidatedInvestigation is not { } after || before <= 0)
        {
            return (true, false, ["the two costs can't be compared"]);
        }

        var reduction = 1 - (after / before);
        var finding = string.Create(CultureInfo.InvariantCulture, $"per validated investigation: control {before:0.000000} {c.Currency}, cascade {after:0.000000} {k.Currency}, {reduction:P1} lower (needs {MinCostReduction:P0})");
        return (false, reduction < MinCostReduction, [finding]);
    }

    private static (string? Inconclusive, string? Failure, string Finding) Latency(EvaluationReport control, EvaluationReport cascade)
    {
        if (control.Latency.P95 is not { } before || cascade.Latency.P95 is not { } after || before <= 0)
        {
            return ("p95 latency wasn't measured on both sides", null, string.Empty);
        }

        var finding = string.Create(CultureInfo.InvariantCulture, $"p95: control {before} ms, cascade {after} ms (at most {MaxLatencyRatio:0.00}×)");
        return (null, after > before * MaxLatencyRatio ? finding : null, finding);
    }
}
