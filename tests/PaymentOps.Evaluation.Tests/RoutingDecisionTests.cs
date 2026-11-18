using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Safety;
using PaymentOps.Evaluation.Scoring;

namespace PaymentOps.Evaluation.Tests;

/// <summary>ADR 0023's rule, step by step, in its pre-registered order: invalid or inconclusive never adopts.</summary>
public sealed class RoutingDecisionTests
{
    private const string Vrr = MetricNames.ValidatedResultRate;
    private const string Fp = MetricNames.ProposalFalsePositiveRate;

    private static readonly ModelIdentity Mini = new("chat-blue", "gpt-5.4-mini", "2026-03-17", "GlobalStandard", "strict");
    private static readonly ModelIdentity Nano = new("chat-small", "gpt-5.4-nano", "2026-03-17", "GlobalStandard", "strict");
    private static readonly ModelIdentity ScreenModel = new("screen", "gpt-5.4-mini", "2026-03-17", "GlobalStandard", "strict");

    // Margins: validated results max(1.6 × 0.02, 1/20) = 0.05; false positives max(1.6 × 0.01, 1/20) = 0.05.
    // Retrieval sits in the same thresholds file but is measured by its own evaluation, never compared here.
    private static Thresholds Frozen(double vrrThreshold = 0.90, double fpThreshold = 0.10) => new(2, "baseline", "rule", new Dictionary<string, ThresholdEntry>
    {
        [Vrr] = new(vrrThreshold, ThresholdRule.Higher, 20, 0.9, 0.95, 1, 0.95, 0.02, [0.95, 0.95]),
        [Fp] = new(fpThreshold, ThresholdRule.Lower, 20, 0, 0.05, 0.1, 0.05, 0.01, [0.05, 0.05]),
        [MetricNames.RetrievalRecallAt5] = new(0.8, ThresholdRule.Higher, 10, 0.9, 0.9, 0.9, null, null, [0.9]),
    });

    private const string ThresholdsHash = "sha256:thresholds";
    private const string SafetyHash = "sha256:safety";

    private static readonly RoutingEvidence Evidence = new(ThresholdsHash, SafetyHash, new HashSet<string> { "reg-1", "held-1" });

    private static ReportInputs Inputs(string routing, bool safety = false) => new(
        new Dictionary<string, string> { [safety ? "safety.json" : "investigations-regression.json"] = safety ? SafetyHash : "sha256:d1" },
        [new PromptHash("investigation-agent", "1.2.0", "sha256:p1")],
        "sha256:release",
        Mini,
        Nano,
        ScreenModel,
        routing,
        new IndexIdentity("knowledge-eval-v1", "sha256:index", 40, true, null),
        "sha256:prices",
        safety ? null : ThresholdsHash);

    private static EvaluationReport Report(string routing, double[]? vrr = null, double[]? fp = null, long p95 = 10_000, decimal? perValidated = 0.0100m, string status = AllInCost.Priced, string commit = "c1")
    {
        var baseline = EvaluationReportTests.Report(EvaluationReportTests.Run(), EvaluationReportTests.Run(), EvaluationReportTests.Run());
        vrr ??= [0.95, 0.95, 0.95];
        fp ??= [0.05, 0.05, 0.05];
        return baseline with
        {
            Runs = [.. baseline.Runs.Select((run, i) => run with
            {
                Metrics = new Dictionary<string, double?> { [Vrr] = vrr[i], [Fp] = fp[i] },
                Cases = [.. run.Cases.Select(c => c with { Escalation = routing == "cascade" && i == 0 && c.CaseId == "reg-1" ? "escalated" : "none" })],
            })],
            Latency = new Percentiles(5_000, p95),
            Cost = baseline.Cost with { AllIn = perValidated * 6, AllInPerValidatedInvestigation = perValidated, AllInStatus = status, Currency = "USD" },
            Source = new ReportSource(commit, "tree-1", false),
            Inputs = Inputs(routing),
            Gate = new GateReport(GateReport.Passed, [], "eval/thresholds.json", new Dictionary<string, double>()),
        };
    }

    private static EvaluationReport Safety(Func<RunReport, RunReport>? change = null)
    {
        var baseline = Report("cascade");
        return baseline with
        {
            System = EvaluationReport.SafetySystem,
            Runs = [.. baseline.Runs.Select(run => (change ?? (r => r))(run with { SafetyHardGates = new SafetyHardGates([], [], []) }))],
            Inputs = Inputs("cascade", safety: true),
        };
    }

    private static RoutingDecision.Result Decide(EvaluationReport? control = null, EvaluationReport? cascade = null, EvaluationReport? safety = null, Thresholds? thresholds = null, bool noSafety = false, RoutingEvidence? evidence = null) =>
        RoutingDecision.Decide(control ?? Report("off"), cascade ?? Report("cascade", perValidated: 0.0070m, p95: 12_000), noSafety ? null : safety ?? Safety(), thresholds ?? Frozen(), evidence ?? Evidence);

    private static EvaluationReport Cascade() => Report("cascade", perValidated: 0.0070m);

    private static void Invalid(RoutingDecision.Result result) => Assert.Equal((RoutingDecision.Invalid, 2), (result.Verdict, result.ExitCode));

    [Theory]
    [InlineData("control")]
    [InlineData("cascade")]
    [InlineData("safety")]
    public void Fewer_than_three_runs_on_any_side_is_invalid(string side)
    {
        static EvaluationReport Two(EvaluationReport r) => r with { Runs = [.. r.Runs.Take(2)] };

        Invalid(side switch
        {
            "control" => Decide(control: Two(Report("off"))),
            "cascade" => Decide(cascade: Two(Cascade())),
            _ => Decide(safety: Two(Safety())),
        });
    }

    [Theory]
    [InlineData("control", 1, Vrr)]
    [InlineData("cascade", 2, Fp)]
    public void A_compared_metric_missing_from_any_run_is_invalid(string side, int run, string metric)
    {
        static EvaluationReport Without(EvaluationReport r, int run, string metric) => r with
        {
            Runs = [.. r.Runs.Select(x => x.Run == run ? x with { Metrics = x.Metrics.Where(m => m.Key != metric).ToDictionary() } : x)],
        };

        var result = side == "control" ? Decide(control: Without(Report("off"), run, metric)) : Decide(cascade: Without(Cascade(), run, metric));

        Invalid(result);
        Assert.Contains($"{side} run {run} didn't measure {metric}", Assert.Single(result.Checks, c => c.Step == "evidence").Findings);
    }

    [Theory]
    [InlineData("missing from one run")]
    [InlineData("missing from every run")]
    [InlineData("repeated in a run")]
    public void A_case_missing_or_repeated_is_invalid(string problem)
    {
        var cascade = Cascade();
        cascade = problem switch
        {
            "missing from one run" => cascade with { Runs = [.. cascade.Runs.Select(r => r.Run == 3 ? r with { Cases = [.. r.Cases.Skip(1)] } : r)] },
            "missing from every run" => cascade with { Runs = [.. cascade.Runs.Select(r => r with { Cases = [.. r.Cases.Skip(1)] })] },
            _ => cascade with { Runs = [.. cascade.Runs.Select(r => r.Run == 1 ? r with { Cases = [.. r.Cases, r.Cases[0]] } : r)] },
        };

        Invalid(Decide(cascade: cascade));
    }

    [Theory]
    [InlineData(GateReport.Failed)]
    [InlineData(GateReport.ReportOnly)]
    public void A_control_without_a_passing_release_verdict_is_invalid(string verdict)
    {
        var control = Report("off");

        Invalid(Decide(control: control with { Gate = control.Gate with { Verdict = verdict } }));
    }

    [Fact]
    public void An_altered_safety_dataset_is_invalid() =>
        Invalid(Decide(evidence: Evidence with { SafetyDatasetSha256 = "sha256:edited" }));

    [Fact]
    public void Safety_cases_that_are_not_the_frozen_datasets_are_invalid() =>
        Invalid(Decide(evidence: Evidence with { SafetyCases = new HashSet<string> { "reg-1", "held-1", "inj-9" } }));

    // The same costs, priced from a different USD list, aren't comparable evidence.
    [Fact]
    public void Costs_from_different_price_lists_are_invalid()
    {
        var cascade = Cascade();

        Invalid(Decide(cascade: cascade with { Inputs = cascade.Inputs! with { Pricing = "sha256:other-prices" } }));
    }

    [Fact]
    public void A_substituted_thresholds_file_is_invalid() =>
        Invalid(Decide(evidence: Evidence with { ThresholdsSha256 = "sha256:weaker" }));

    [Fact]
    public void A_cascade_gated_on_other_thresholds_is_invalid()
    {
        var cascade = Cascade();

        Invalid(Decide(cascade: cascade with { Inputs = cascade.Inputs! with { Thresholds = "sha256:weaker" } }));
    }

    [Fact]
    public void Retrieval_thresholds_are_not_compared_here() =>
        Assert.DoesNotContain(MetricNames.RetrievalRecallAt5, RoutingDecision.Compared(Frozen()));

    [Fact]
    public void Adopts_when_every_condition_holds()
    {
        var result = Decide();

        Assert.Equal((RoutingDecision.Adopt, 0), (result.Verdict, result.ExitCode));
        Assert.All(result.Checks, c => Assert.True(c.Passed));
    }

    [Fact]
    public void Equivalent_trees_with_different_commits_are_comparable() =>
        Assert.Equal(RoutingDecision.Adopt, Decide(cascade: Report("cascade", perValidated: 0.0070m, commit: "c2")).Verdict);

    [Theory]
    [InlineData("tree")]
    [InlineData("dataset")]
    [InlineData("prompt")]
    [InlineData("release")]
    [InlineData("chat")]
    [InlineData("small")]
    [InlineData("screen")]
    [InlineData("index name")]
    [InlineData("index fingerprint")]
    public void A_different_tree_dataset_prompt_release_model_or_index_is_invalid(string field)
    {
        var cascade = Report("cascade", perValidated: 0.0070m);
        var inputs = cascade.Inputs!;
        cascade = field switch
        {
            "tree" => cascade with { Source = cascade.Source! with { Tree = "tree-2" } },
            "dataset" => cascade with { Inputs = inputs with { Datasets = new Dictionary<string, string> { ["investigations-regression.json"] = "sha256:d2" } } },
            "prompt" => cascade with { Inputs = inputs with { Prompts = [new PromptHash("investigation-agent", "1.2.0", "sha256:p2")] } },
            "release" => cascade with { Inputs = inputs with { Release = "sha256:other" } },
            "chat" => cascade with { Inputs = inputs with { Chat = Mini with { Version = "2026-06-01" } } },
            "small" => cascade with { Inputs = inputs with { Small = Nano with { ContentFilterPolicy = "default" } } },
            "screen" => cascade with { Inputs = inputs with { Screen = ScreenModel with { Deployment = "screen-2" } } },
            "index name" => cascade with { Inputs = inputs with { Index = inputs.Index with { Name = "knowledge-v1" } } },
            _ => cascade with { Inputs = inputs with { Index = inputs.Index with { Fingerprint = "sha256:other" } } },
        };

        var result = Decide(cascade: cascade);

        Assert.Equal((RoutingDecision.Invalid, 2), (result.Verdict, result.ExitCode));
    }

    [Fact]
    public void A_dirty_report_is_invalid() =>
        Assert.Equal(RoutingDecision.Invalid, Decide(control: Report("off") with { Source = new ReportSource("c1", "tree-1", true) }).Verdict);

    [Fact]
    public void A_report_with_an_unstable_index_is_invalid()
    {
        var control = Report("off");

        var result = Decide(control: control with { Inputs = control.Inputs! with { Index = control.Inputs.Index with { Stable = false, Problem = "changed during the run" } } });

        Assert.Equal(RoutingDecision.Invalid, result.Verdict);
    }

    [Theory]
    [InlineData("control")]
    [InlineData("cascade")]
    [InlineData("safety")]
    public void Wrong_routing_mode_on_either_side_is_invalid(string side)
    {
        var result = side switch
        {
            "control" => Decide(control: Report("cascade")),
            "cascade" => Decide(cascade: Report("off")),
            _ => Decide(safety: Safety() with { Inputs = Inputs("off", safety: true) }),
        };

        Assert.Equal(RoutingDecision.Invalid, result.Verdict);
    }

    [Fact]
    public void Investigation_metrics_pass_but_safety_fails_rejects()
    {
        var safety = Safety(run => run.Run == 2 ? run with { SafetyHardGates = new SafetyHardGates([new Violation("inj-1", "followed the planted instruction")], [], []) } : run);

        var result = Decide(safety: safety);

        Assert.Equal((RoutingDecision.Reject, 1), (result.Verdict, result.ExitCode));
        Assert.Contains(result.Checks, c => c.Step == "safety" && !c.Passed);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("missing case")]
    [InlineData("missing run")]
    [InlineData("spoiled run")]
    [InlineData("other tree")]
    [InlineData("routing off")]
    public void Missing_incomplete_or_incompatible_safety_report_is_invalid(string problem)
    {
        var safety = Safety();
        var result = problem switch
        {
            "absent" => Decide(noSafety: true),
            "missing case" => Decide(safety: Safety(run => run.Run == 1 ? run with { Cases = [.. run.Cases.Skip(1)] } : run)),
            "missing run" => Decide(safety: safety with { Runs = [.. safety.Runs.Take(2)] }),
            "spoiled run" => Decide(safety: Safety(run => run with { Cases = [run.Cases[0] with { InfrastructureFailure = "busy" }, .. run.Cases.Skip(1)] })),
            "other tree" => Decide(safety: safety with { Source = new ReportSource("c3", "tree-2", false) }),
            _ => Decide(safety: safety with { Inputs = Inputs("off", safety: true) }),
        };

        Assert.Equal((RoutingDecision.Invalid, 2), (result.Verdict, result.ExitCode));
    }

    [Fact]
    public void Median_passes_but_one_run_fails_rejects()
    {
        var result = Decide(cascade: Report("cascade", vrr: [0.95, 0.95, 0.85], perValidated: 0.0070m));

        Assert.Equal(RoutingDecision.Reject, result.Verdict);
        Assert.Contains("run 3: validated_result_rate 0.850", Assert.Single(result.Checks, c => c.Step == "per_run_thresholds").Findings[0], StringComparison.Ordinal);
    }

    private static readonly double[] OneRunFailing = [0.95, 0.95, 0.85];

    [Fact]
    public void Release_gate_still_judges_the_median()
    {
        var runs = OneRunFailing.Select(v => new RunMetrics(new Dictionary<string, double?> { [Vrr] = v }, new HardGates([], []), new Dictionary<string, IReadOnlyDictionary<string, bool>>())).ToList();
        var summary = new Dictionary<string, MetricSummary> { [Vrr] = new(0.95, 0.85, 0.95, 20) };
        var thresholds = new Thresholds(2, "b", "r", new Dictionary<string, ThresholdEntry> { [Vrr] = Frozen().Metrics[Vrr] });

        Assert.True(Scoring.Gate.Evaluate(runs, summary, thresholds, new HashSet<string> { Vrr }).Passed);
    }

    [Theory]
    [InlineData(0.89, 0.05, false)]
    [InlineData(0.90, 0.05, true)]
    [InlineData(0.95, 0.11, false)]
    [InlineData(0.95, 0.10, true)]
    public void Quality_margin_holds_in_each_metrics_direction(double vrr, double fp, bool held)
    {
        // Thresholds low enough that only the margin can fail: control medians 0.95 and 0.05.
        var cascade = Report("cascade", vrr: [vrr, vrr, vrr], fp: [fp, fp, fp], perValidated: 0.0070m);

        var result = Decide(cascade: cascade, thresholds: Frozen(vrrThreshold: 0.5, fpThreshold: 0.5));

        Assert.Equal(held, Assert.Single(result.Checks, c => c.Step == "quality_margin").Passed);
        Assert.Equal(held ? RoutingDecision.Adopt : RoutingDecision.Reject, result.Verdict);
    }

    [Theory]
    [InlineData("0.007510", RoutingDecision.Reject)]
    [InlineData("0.007500", RoutingDecision.Adopt)]
    public void Cost_24_9_percent_rejects(string cascadeCost, string verdict) =>
        Assert.Equal(verdict, Decide(cascade: Report("cascade", perValidated: decimal.Parse(cascadeCost, System.Globalization.CultureInfo.InvariantCulture))).Verdict);

    [Theory]
    [InlineData(AllInCost.UsageIncomplete)]
    [InlineData(AllInCost.UsageMissing)]
    [InlineData("unpriced:chat-small")]
    public void Cost_inconclusive_is_invalid_never_adopt(string status)
    {
        var result = Decide(cascade: Report("cascade", perValidated: null, status: status));

        Assert.Equal((RoutingDecision.Inconclusive, 2), (result.Verdict, result.ExitCode));
    }

    [Theory]
    [InlineData(12_510, RoutingDecision.Reject)]
    [InlineData(12_500, RoutingDecision.Adopt)]
    public void Latency_25_1_percent_rejects(long cascadeP95, string verdict) =>
        Assert.Equal(verdict, Decide(cascade: Report("cascade", perValidated: 0.0070m, p95: cascadeP95)).Verdict);

    [Fact]
    public void Summary_states_per_run_values_escalation_rate_and_the_sample_caveat()
    {
        var cascade = Report("cascade", vrr: [0.95, 0.95, 0.85], perValidated: 0.0070m);
        var result = Decide(cascade: cascade);

        var summary = RoutingDecisionCommand.Summary(result, Report("off"), cascade, Frozen());

        Assert.Contains("0.950, 0.950, 0.850", summary, StringComparison.Ordinal);
        Assert.Contains("Escalated 1 of 6 cascade investigations", summary, StringComparison.Ordinal);
        Assert.Contains("2 cases × 3 runs", summary, StringComparison.Ordinal);
    }

    private static string Temp(string text)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData("0.0070", 0)]
    [InlineData("0.0090", 1)]
    public async Task The_command_reads_the_reports_and_exits_with_the_verdict(string cascadeCost, int exit)
    {
        var thresholds = Temp(Frozen().ToJson());
        var dataset = Temp("""{ "cases": [ { "id": "reg-1" }, { "id": "held-1" } ] }""");
        EvaluationReport Bound(EvaluationReport r, bool safety = false) => r with
        {
            Inputs = r.Inputs! with
            {
                Thresholds = safety ? null : ReportInputs.Sha256Of(thresholds),
                Datasets = safety ? new Dictionary<string, string> { ["safety.json"] = ReportInputs.Sha256Of(dataset) } : r.Inputs.Datasets,
            },
        };
        var cascade = Report("cascade", perValidated: decimal.Parse(cascadeCost, System.Globalization.CultureInfo.InvariantCulture));
        var (output, summary) = (Path.GetTempFileName(), Path.GetTempFileName());

        var code = await RoutingDecisionCommand.RunAsync(
            ["--control", Temp(Bound(Report("off")).ToJson()), "--cascade", Temp(Bound(cascade).ToJson()), "--cascade-safety", Temp(Bound(Safety(), safety: true).ToJson()),
             "--thresholds", thresholds, "--safety-dataset", dataset, "--out", output, "--summary", summary]);

        Assert.Equal(exit, code);
        Assert.Contains(exit == 0 ? "adopt" : "reject", File.ReadAllText(output), StringComparison.Ordinal);
    }
}
