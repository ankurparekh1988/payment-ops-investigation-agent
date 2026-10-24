using System.Text.Json.Nodes;

using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Retrieval;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

public sealed class CommandTests
{
    private static readonly string Pricing = Path.Combine(RepositoryRoot.FullPath, "eval", "pricing.json");
    private static readonly string Sources = Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src");

    private static string Temp(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    private static string Dataset() => Temp(new JsonObject
    {
        ["schemaVersion"] = 2,
        ["description"] = "test",
        ["cases"] = new JsonArray(JsonNode.Parse("""
            { "id": "c1", "group": "core", "access": "general", "role": "reader", "question": "Why was M-1042 paid late?",
              "allowedOutcomes": ["assessed"], "requiredTools": ["get_merchant_profile"], "toolArguments": [], "scoreEvidence": false,
              "evidenceOutcomes": null, "expectedEvidence": [], "requiredSections": [], "acceptableSections": [], "proposal": "either",
              "restrictedMarkers": [], "expectedRootCause": null }
            """)),
    }.ToJsonString());

    private static string[] Args(string dataset, params string[] extra) =>
    [
        "--foundry", "https://foundry.invalid/", "--search", "https://search.invalid", "--scope", "https://cognitiveservices.azure.com/.default",
        "--ai", Path.Combine(RepositoryRoot.FullPath, "ai"), "--sources", Sources, "--dataset", dataset, "--runs", "3", "--system", "agent", "--pricing", Pricing,
        "--out", Path.GetTempFileName(), "--summary", Path.GetTempFileName(), .. extra,
    ];

    private static Task<int> Investigations(string[] args, params RunOutcome[] runs) =>
        InvestigationEvalCommand.RunAsync(args, (_, _) => new ScriptedRunner(runs), _ => null, TimeSpan.Zero);

    private static RunOutcome Run(bool leak = false) => new(
        [new CaseResult("c1", "assessed") { RetrievedSections = leak ? [new SectionHit("kr", "restricted")] : [] }], 0);

    private static readonly RunOutcome Invalid = new([new CaseResult("c1", "not-assessed:busy") { InfrastructureFailure = "busy" }], 0);

    [Fact]
    public async Task Exit_codes_are_0_pass_1_fail_2_usage_3_could_not_evaluate()
    {
        var dataset = Dataset();

        Assert.Equal(0, await Investigations(Args(dataset), Run(), Run(), Run()));
        Assert.Equal(1, await Investigations(Args(dataset), Run(), Run(leak: true), Run()));
        Assert.Equal(2, await Investigations(["--foundry", "http://not-https"]));
        Assert.Equal(2, await Investigations(Args(dataset, "--colour", "blue")));
        Assert.Equal(3, await Investigations(Args(dataset), Run(), Invalid, Invalid));
    }

    [Fact]
    public async Task A_malformed_dataset_thresholds_or_pricing_file_exits_2()
    {
        var dataset = Dataset();
        var malformed = Temp("{ \"schemaVersion\": 2, ");

        Assert.Equal(2, await Investigations(Args(malformed), Run()));
        Assert.Equal(2, await Investigations(Args(dataset, "--thresholds", malformed), Run()));
        Assert.Equal(2, await Investigations([.. Args(dataset)[..^4], "--pricing", malformed, "--out", Path.GetTempFileName(), "--summary", Path.GetTempFileName()], Run()));
    }

    [Fact]
    public async Task A_thresholds_file_missing_an_applicable_metric_exits_2_before_any_run()
    {
        var thresholds = Temp(new Thresholds(1, new string('a', 40), ThresholdRule.Text, new Dictionary<string, ThresholdEntry>
        {
            [MetricNames.RetrievalRecallAt5] = new(0.9, ThresholdRule.Higher, 30, 0.95, 0.95, 0.95),
        }).ToJson());
        var runner = new ScriptedRunner(Run());

        var exit = await InvestigationEvalCommand.RunAsync(Args(Dataset(), "--thresholds", thresholds), (_, _) => runner, _ => null, TimeSpan.Zero);

        Assert.Equal(2, exit);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void A_repeated_option_collects_every_dataset()
    {
        var options = Options.Parse(["--dataset", "a.json", "--runs", "3", "--dataset", "b.json", "--no-judge"], ["dataset", "runs"], ["no-judge"]);

        Assert.Equal(["a.json", "b.json"], options.All("dataset"));
        Assert.True(options.Flag("no-judge"));
        Assert.Throws<UsageException>(() => Options.Parse(["--runs"], ["runs"], []));
        Assert.Throws<UsageException>(() => options.One("dataset"));
    }

    [Fact]
    public async Task Thresholds_command_derives_every_gating_metric_from_the_baseline_report()
    {
        var investigations = Temp(EvaluationReportTests.Report(EvaluationReportTests.Run(), EvaluationReportTests.Run("incomplete"), EvaluationReportTests.Run()).ToJson());
        var retrieval = Temp(RetrievalReport(recall: 0.9).ToJson());
        var output = Path.GetTempFileName();

        Assert.Equal(0, await ThresholdsCommand.RunAsync(["--investigations", investigations, "--retrieval", retrieval, "--out", output]));

        var thresholds = Thresholds.Load(output);
        Assert.Equal(new string('c', 40), thresholds.BaselineCommit);
        Assert.Equal(ThresholdRule.Text, thresholds.Rule);
        Assert.Equal(ThresholdRule.Derive(MetricNames.ExpectedEvidenceCoverage, [1, 0, 1], 1), thresholds.Metrics[MetricNames.ExpectedEvidenceCoverage].Threshold);
        Assert.Equal(ThresholdRule.Derive(MetricNames.RetrievalRecallAt5, [0.9], 10), thresholds.Metrics[MetricNames.RetrievalRecallAt5].Threshold);
        Assert.DoesNotContain(MetricNames.ToolArgumentAccuracy, thresholds.Metrics.Keys);
        Assert.DoesNotContain(MetricNames.ProposalRecall, thresholds.Metrics.Keys);
    }

    [Fact]
    public async Task Thresholds_come_only_from_a_three_run_agent_baseline()
    {
        var oneRun = Temp(EvaluationReportTests.Report(EvaluationReportTests.Run()).ToJson());
        var retrieval = Temp(RetrievalReport(recall: 0.9).ToJson());

        Assert.Equal(2, await ThresholdsCommand.RunAsync(["--investigations", oneRun, "--retrieval", retrieval, "--out", Path.GetTempFileName()]));
    }

    [Fact]
    public async Task Retrieval_eval_reports_recall_at_5_in_the_selected_mode_only()
    {
        var modes = new List<RetrievalMode>();
        var dataset = new CalibrationDataset(1,
        [
            new CalibrationCase("hit", CaseKind.Critical, "general", "q1", [new ExpectedSection(GeneralSection.DocId, GeneralSection.SectionPath)]),
            new CalibrationCase("miss", CaseKind.Paraphrase, "general", "q2", [new ExpectedSection(GeneralSection.DocId, GeneralSection.SectionPath)]),
            new CalibrationCase("probe", CaseKind.Probe, "general", "q3", []),
        ]);
        var keys = new Dictionary<ExpectedSection, string> { [dataset.Cases[0].Expected[0]] = GeneralSection.Key };

        var result = await RetrievalEval.RunAsync(dataset, keys, Release, (question, _, mode) =>
        {
            modes.Add(mode);
            return Task.FromResult<IReadOnlyList<RetrievedSection>>(question == "q1" ? [GeneralSection] : []);
        });

        Assert.All(modes, m => Assert.Equal(Release.RetrievalMode, m));
        Assert.Equal(3, modes.Count);
        Assert.Equal(0.5, result.Metrics.Values[MetricNames.RetrievalRecallAt5]);
        Assert.Equal(0.5, result.Metrics.Values[MetricNames.RetrievalMrr]);
        Assert.True(result.Metrics.Hard.Passed);
    }

    [Fact]
    public async Task A_probe_returning_restricted_content_fails_the_retrieval_gate()
    {
        var dataset = new CalibrationDataset(1, [new CalibrationCase("probe", CaseKind.Probe, "general", "q3", [])]);

        var result = await RetrievalEval.RunAsync(dataset, new Dictionary<ExpectedSection, string>(), Release, (_, _, _) =>
            Task.FromResult<IReadOnlyList<RetrievedSection>>([RestrictedSection]));

        Assert.Equal("probe", Assert.Single(result.Metrics.Hard.AuthorizationLeaks).CaseId);
    }

    [Fact]
    public async Task Kill_test_command_records_the_decision_from_two_reports()
    {
        var agent = EvaluationReportTests.Report(EvaluationReportTests.Run(), EvaluationReportTests.Run(), EvaluationReportTests.Run());
        var challenger = agent with { System = "fixed-workflow" };
        var output = Path.GetTempFileName();
        var summary = Path.GetTempFileName();

        var exit = await KillTestCommand.RunAsync(["--agent", Temp(agent.ToJson()), "--challenger", Temp(challenger.ToJson()), "--out", output, "--summary", summary]);

        Assert.Equal(0, exit);
        Assert.Equal("KeepAgentMixed", (string?)JsonNode.Parse(File.ReadAllText(output))!["result"]);
        Assert.Contains("KeepAgentMixed", File.ReadAllText(summary), StringComparison.Ordinal);
        Assert.Equal(2, await KillTestCommand.RunAsync(["--agent", Temp(agent.ToJson()), "--challenger", Temp(agent.ToJson()), "--out", output, "--summary", summary]));
    }

    private static EvaluationReport RetrievalReport(double recall) => EvaluationReport.ForRetrieval(
        EvaluationReportTests.Context with { System = EvaluationReport.RetrievalSystem },
        new RetrievalResult(
            new RunMetrics(
                new Dictionary<string, double?> { [MetricNames.RetrievalRecallAt5] = recall, [MetricNames.RetrievalMrr] = recall },
                new HardGates([], []),
                new Dictionary<string, IReadOnlyDictionary<string, bool>>()),
            10,
            []),
        null,
        null);
}
