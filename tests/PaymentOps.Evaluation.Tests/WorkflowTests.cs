using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

using YamlDotNet.RepresentationModel;

namespace PaymentOps.Evaluation.Tests;

/// <summary>The AI evaluation workflow's shape and its verdict, read from the YAML that runs.</summary>
public sealed partial class WorkflowTests
{
    private static readonly string WorkflowPath = Path.Combine(RepositoryRoot.FullPath, ".github", "workflows", "ai-evaluation.yml");

    private static readonly YamlMappingNode Workflow = Load();

    private static readonly string[] EvaluateJobs = ["evaluate", "evaluate-safety"];

    private static readonly string[] ReportNames = ["investigations", "retrieval", "safety"];

    // The jobs whose results one evaluation combines, so all must come from the same attempt.
    private static readonly string[] AttemptJobs = ["publish-eval-index", "evaluate", "evaluate-safety"];

    private static YamlMappingNode Jobs => (YamlMappingNode)Workflow["jobs"];

    private static YamlMappingNode Load()
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(WorkflowPath);
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static YamlMappingNode Job(string id) => (YamlMappingNode)Jobs[id];

    private static IEnumerable<(string Id, YamlMappingNode Job)> AllJobs() =>
        Jobs.Children.Select(j => (((YamlScalarNode)j.Key).Value!, (YamlMappingNode)j.Value));

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value : null;

    private static IEnumerable<YamlMappingNode> Steps(YamlMappingNode job) => ((YamlSequenceNode)job["steps"]).Children.Cast<YamlMappingNode>();

    private static YamlMappingNode Step(string job, string id) => Steps(Job(job)).Single(s => Scalar(s, "id") == id);

    private static bool Uses(YamlMappingNode step, string action) =>
        (Scalar(step, "uses") ?? string.Empty).StartsWith(action + "@", StringComparison.Ordinal);

    private static Dictionary<string, string> Permissions(YamlMappingNode job) =>
        job.Children.TryGetValue(new YamlScalarNode("permissions"), out var p) && p is YamlMappingNode map
            ? map.Children.ToDictionary(e => ((YamlScalarNode)e.Key).Value!, e => ((YamlScalarNode)e.Value).Value!, StringComparer.Ordinal)
            : [];

    private static bool HasPermissions(YamlMappingNode job) => job.Children.ContainsKey(new YamlScalarNode("permissions"));

    private static bool RunsOnPullRequests(YamlMappingNode job) =>
        !(Scalar(job, "if") ?? string.Empty).Contains("github.event_name != 'pull_request'", StringComparison.Ordinal);

    [Fact]
    public void The_required_job_is_named_AI_evaluation()
    {
        var named = AllJobs().Where(j => Scalar(j.Job, "name") == "AI evaluation").ToList();

        Assert.Equal("verdict", Assert.Single(named).Id);
    }

    [Fact]
    public void It_runs_on_every_pull_request_with_no_paths_filter_and_never_pull_request_target()
    {
        var on = (YamlMappingNode)Workflow["on"];
        var text = File.ReadAllText(WorkflowPath);

        Assert.True(on.Children.ContainsKey(new YamlScalarNode("pull_request")));
        Assert.True(on.Children.ContainsKey(new YamlScalarNode("schedule")));
        Assert.True(on.Children.ContainsKey(new YamlScalarNode("workflow_dispatch")));
        Assert.DoesNotContain("pull_request_target", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"^\s*paths(-ignore)?:", RegexOptions.Multiline), text);
        Assert.Equal("0 6 1 * *", Scalar((YamlMappingNode)((YamlSequenceNode)on["schedule"]).Children[0], "cron"));
    }

    // Adding the label is the request, so the label event must start the workflow, as must every push after it.
    [Fact]
    public void A_pull_request_runs_on_the_label_as_well_as_each_push()
    {
        var types = ((YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)Workflow["on"])["pull_request"])["types"]).Children
            .Select(t => ((YamlScalarNode)t).Value).ToList();

        Assert.Equal(["opened", "synchronize", "reopened", "labeled"], types);
    }

    // Each evaluation costs money, so a pull request is evaluated only once it carries the label; the schedule always is.
    [Fact]
    public void Evaluation_runs_only_when_requested()
    {
        Assert.Equal(
            "${{ github.event_name != 'pull_request' || contains(github.event.pull_request.labels.*.name, 'evaluate') }}",
            Scalar((YamlMappingNode)Job("classify")["outputs"], "requested"));
        foreach (var job in AttemptJobs)
        {
            Assert.Contains("needs.classify.outputs.requested == 'true'", Scalar(Job(job), "if"), StringComparison.Ordinal);
        }

        Assert.Equal("${{ needs.classify.outputs.requested }}", Scalar((YamlMappingNode)Step("verdict", "verdict")["env"], "REQUESTED"));
    }

    [Fact]
    public void One_concurrency_group_never_cancels_a_run_in_progress()
    {
        var concurrency = (YamlMappingNode)Workflow["concurrency"];

        Assert.StartsWith("ai-eval-", Scalar(concurrency, "group"), StringComparison.Ordinal);
        Assert.Equal("false", Scalar(concurrency, "cancel-in-progress"));

        // Without a queue, a newer pending run cancels the older one while a run waits days for approval.
        Assert.Equal("max", Scalar(concurrency, "queue"));
    }

    [Fact]
    public void Publish_and_evaluate_use_different_environments()
    {
        Assert.Contains("-eval-publish", Scalar(Job("publish-eval-index"), "environment"), StringComparison.Ordinal);
        Assert.DoesNotContain("-eval-publish", Scalar(Job("evaluate"), "environment"), StringComparison.Ordinal);
        Assert.Contains("-eval", Scalar(Job("evaluate"), "environment"), StringComparison.Ordinal);
        Assert.Contains("-eval-publish-scheduled", Scalar(Job("record"), "environment"), StringComparison.Ordinal);
        foreach (var (id, job) in AllJobs())
        {
            var text = Serialize(job);
            Assert.False(text.Contains("AZURE_EVAL_CLIENT_ID", StringComparison.Ordinal) && text.Contains("AZURE_EVAL_PUBLISH_CLIENT_ID", StringComparison.Ordinal), id);
        }

        Assert.Contains("AZURE_EVAL_PUBLISH_CLIENT_ID", Serialize(Job("publish-eval-index")), StringComparison.Ordinal);
        Assert.Contains("AZURE_EVAL_CLIENT_ID", Serialize(Job("evaluate")), StringComparison.Ordinal);
    }

    // Neither waits for the other, and both wait on the same jobs in the same environment, so one approval starts
    // both; they read the same index as the same identity.
    [Fact]
    public void The_safety_evaluation_runs_alongside_the_investigations_under_the_same_condition()
    {
        var investigations = Job("evaluate");
        var safety = Job("evaluate-safety");

        Assert.Equal(Serialize(investigations["needs"]), Serialize(safety["needs"]));
        Assert.Equal(Scalar(investigations, "if"), Scalar(safety, "if"));
        Assert.Equal(Scalar(investigations, "environment"), Scalar(safety, "environment"));
        Assert.Equal(Permissions(investigations), Permissions(safety));
        Assert.Equal(Scalar((YamlMappingNode)Step("evaluate", "evaluate")["env"], "INDEX"), Scalar((YamlMappingNode)Step("evaluate-safety", "evaluate")["env"], "INDEX"));
        Assert.Contains("AZURE_EVAL_CLIENT_ID", Serialize(safety), StringComparison.Ordinal);
    }

    [Fact]
    public void No_job_that_runs_on_a_pull_request_has_repository_write_permission()
    {
        Assert.Equal("read", Scalar((YamlMappingNode)Workflow["permissions"], "contents"));
        foreach (var (id, job) in AllJobs().Where(j => RunsOnPullRequests(j.Job)))
        {
            Assert.True(HasPermissions(job), $"{id} must state its permissions");
            Assert.All(Permissions(job).Where(p => p.Key != "id-token"), p => Assert.NotEqual("write", p.Value));
        }

        Assert.True(HasPermissions(Job("verdict")));
        Assert.Empty(Permissions(Job("verdict")));
    }

    [Fact]
    public void Only_report_scheduled_writes_issues_and_no_job_writes_contents()
    {
        var writers = AllJobs().Where(j => Permissions(j.Job).GetValueOrDefault("issues") == "write").Select(j => j.Id).ToList();

        Assert.Equal(["report-scheduled"], writers);
        Assert.Contains("github.event_name != 'pull_request'", Scalar(Job("report-scheduled"), "if"), StringComparison.Ordinal);
        Assert.All(AllJobs(), j => Assert.NotEqual("write", Permissions(j.Job).GetValueOrDefault("contents")));
    }

    [Fact]
    public void A_base_without_a_classifier_requires_nothing()
    {
        var script = Scalar(Step("classify", "classify"), "run")!;

        Assert.Contains("not required: the gate is introduced by this change", script, StringComparison.Ordinal);
        // A renamed classifier would make every later pull request "not required", so the path it checks must exist.
        var check = Scalar(Step("classify", "base"), "run")!;
        Assert.Contains("[[ -f \"$RUNNER_TEMP/base/eval/PaymentOps.Evaluation/Commands/ClassifyChangeCommand.cs\" ]]", check, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot.FullPath, "eval", "PaymentOps.Evaluation", "Commands", "ClassifyChangeCommand.cs")));
    }

    [Fact]
    public void The_classifier_uses_the_base_toolchain()
    {
        var setups = AllJobs().SelectMany(j => Steps(j.Job).Select(s => (j.Id, Step: s)))
            .Where(s => Uses(s.Step, "actions/setup-dotnet"))
            .ToList();

        // The base is checked out outside the workspace, so no file from the pull request reaches its build.
        var classify = Assert.Single(setups, s => s.Id == "classify");
        Assert.Equal("${{ runner.temp }}/base/global.json", Scalar((YamlMappingNode)classify.Step["with"], "global-json-file"));
        var script = Scalar(Step("classify", "classify"), "run")!;
        Assert.Contains("cd \"$RUNNER_TEMP/base\"", script, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project eval/PaymentOps.Evaluation", script, StringComparison.Ordinal);
        Assert.Contains("git worktree add \"$RUNNER_TEMP/base\"", Scalar(Step("classify", "base"), "run"), StringComparison.Ordinal);
        Assert.All(setups.Where(s => s.Id != "classify"), s => Assert.Equal("global.json", Scalar((YamlMappingNode)s.Step["with"], "global-json-file")));
    }

    [Theory]
    [InlineData("evaluate")]
    [InlineData("evaluate-safety")]
    public void Evaluation_passes_the_content_safety_endpoint(string job)
    {
        var step = Step(job, "evaluate");

        Assert.Contains("--content-safety \"$CONTENT_SAFETY_ENDPOINT\"", Scalar(step, "run"), StringComparison.Ordinal);
        Assert.Equal("${{ vars.EVAL_CONTENT_SAFETY_ENDPOINT }}", Scalar((YamlMappingNode)step["env"], "CONTENT_SAFETY_ENDPOINT"));
    }

    [Fact]
    public void The_changed_paths_list_both_sides_of_a_rename_unquoted()
    {
        Assert.Contains("git -c core.quotePath=false diff --no-renames --name-only", Scalar(Step("classify", "classify"), "run"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_verdict_reads_outputs_not_job_results()
    {
        var step = Step("verdict", "verdict");
        var text = Serialize(step);

        Assert.DoesNotContain(".result", text, StringComparison.Ordinal);
        foreach (Match match in NeedsOutput().Matches(text))
        {
            var outputs = (YamlMappingNode)Job(match.Groups["job"].Value)["outputs"];
            Assert.True(outputs.Children.ContainsKey(new YamlScalarNode(match.Groups["output"].Value)), match.Value);
        }

        Assert.DoesNotContain("${{", Scalar(step, "run"), StringComparison.Ordinal);
    }

    public static TheoryData<string, Dictionary<string, string>, string, string, int> Verdicts() => new()
    {
        { "1 classify incomplete", Env(required: ""), "error", "the evaluation workflow didn't complete", 1 },
        { "2 violation", Env(violation: "a change to the thresholds or the evaluation datasets needs an ADR under docs/adr/ in the same pull request"), "policy-violation", "a change to the thresholds or the evaluation datasets needs an ADR under docs/adr/ in the same pull request", 1 },
        { "2 violation beats not requested", Env(violation: "x", requested: "false", publish: "", investigation: "", retrieval: "", safety: "", login: "", safetyLogin: ""), "policy-violation", "x", 1 },
        { "3 untrusted", Env(trusted: "false"), "untrusted", "reproduce this change on a branch in this repository", 1 },
        { "3 not requested", Env(requested: "false", publish: "", publishAttempt: "", evaluateAttempt: "", safetyAttempt: "", investigation: "", retrieval: "", safety: "", login: "", safetyLogin: "", reportsValid: "false"), "not-requested", "add the evaluate label to run the AI evaluation", 1 },
        { "4 not required needs no label", Env(required: "false", requested: "false", reason: "not required: no AI behaviour or gate policy changed"), "not-required", "not required: no AI behaviour or gate policy changed", 0 },
        { "4 not required", Env(required: "false", reason: "not required: the gate is introduced by this change"), "not-required", "not required: the gate is introduced by this change", 0 },
        { "4 untrusted but not required", Env(required: "false", trusted: "false", reason: "not required: no AI behaviour or gate policy changed"), "not-required", "not required: no AI behaviour or gate policy changed", 0 },
        { "5 scope violation", Env(publish: "scope-violation", investigation: "", retrieval: "", safety: "", login: "", safetyLogin: ""), "scope-violation", "the evaluation publisher can write the live index; fix its role before evaluating", 1 },
        { "6 publish failed", Env(publish: "publish-failed", investigation: "", retrieval: "", safety: "", login: "", safetyLogin: ""), "invalid-input", "invalid evaluation input", 1 },
        { "6 exit 2", Env(investigation: "2"), "invalid-input", "invalid evaluation input", 1 },
        { "7 publish sign-in", Env(publish: "login", investigation: "", retrieval: "", safety: "", login: "", safetyLogin: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 publish unavailable", Env(publish: "unavailable", investigation: "", retrieval: "", safety: "", login: "", safetyLogin: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 evaluate sign-in", Env(login: "failure", investigation: "", retrieval: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 safety sign-in", Env(safetyLogin: "failure", safety: ""), "unavailable", "dev environment unavailable", 1 },
        { "an evaluation from an earlier attempt beats a gate failure", Env(runAttempt: "2", investigation: "1", evaluateAttempt: "1"), "error", "the evaluation jobs ran in different attempts; re-run all jobs", 1 },
        { "safety from an earlier attempt", Env(runAttempt: "2", safetyAttempt: "1"), "error", "the evaluation jobs ran in different attempts; re-run all jobs", 1 },
        { "a publish from an earlier attempt", Env(runAttempt: "2", publishAttempt: "1"), "error", "the evaluation jobs ran in different attempts; re-run all jobs", 1 },
        { "a job that never ran isn't another attempt", Env(safetyAttempt: "", safety: "", safetyLogin: ""), "error", "the evaluation workflow didn't complete", 1 },
        { "index switch: no publish attempt", Env(publish: "", publishAttempt: ""), "passed", "passed", 0 },
        { "every job re-run", Env(runAttempt: "2", publishAttempt: "2", evaluateAttempt: "2", safetyAttempt: "2"), "passed", "passed", 0 },
        { "safety sign-in beats an investigation gate failure", Env(investigation: "1", safetyLogin: "failure", safety: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 exit 3", Env(retrieval: "3"), "unavailable", "dev environment unavailable", 1 },
        { "7 exit 3 stopped the evaluations", Env(investigation: "3", retrieval: ""), "unavailable", "dev environment unavailable", 1 },
        { "6 exit 2 stopped the evaluations", Env(investigation: "2", retrieval: ""), "invalid-input", "invalid evaluation input", 1 },
        { "8 a crash stopped the evaluations", Env(investigation: "134", retrieval: ""), "error", "the evaluation workflow didn't complete", 1 },
        { "8 exit missing", Env(retrieval: ""), "error", "the evaluation workflow didn't complete", 1 },
        { "9 gate failed", Env(investigation: "1"), "failed", "gate failed", 1 },
        { "9 safety gate failed", Env(safety: "1"), "failed", "gate failed", 1 },
        { "6 safety exit 2", Env(safety: "2"), "invalid-input", "invalid evaluation input", 1 },
        { "7 safety exit 3", Env(safety: "3"), "unavailable", "dev environment unavailable", 1 },
        { "8 safety exit missing", Env(safety: ""), "error", "the evaluation workflow didn't complete", 1 },
        { "10 unexpected safety exit", Env(safety: "134"), "error", "the evaluation workflow didn't complete", 1 },
        { "safety unavailable beats an investigation gate failure", Env(investigation: "1", safety: "3"), "unavailable", "dev environment unavailable", 1 },
        { "10 unexpected exit", Env(investigation: "134"), "error", "the evaluation workflow didn't complete", 1 },
        { "11 passed", Env(), "passed", "passed", 0 },
        { "scope violation beats a gate failure", Env(publish: "scope-violation", investigation: "1"), "scope-violation", "the evaluation publisher can write the live index; fix its role before evaluating", 1 },
        { "invalid input beats unavailable", Env(investigation: "2", retrieval: "3"), "invalid-input", "invalid evaluation input", 1 },
        { "index switch skips publishing", Env(publish: ""), "passed", "passed", 0 },
        { "scheduled gate failure, history not recorded", Env("schedule", investigation: "1", reportsValid: "true", recordLogin: "success", recordExit: "3"), "failed", "gate failed; evaluation history wasn't recorded", 1 },
        { "scheduled pass, record exit 3", Env("schedule", reportsValid: "true", recordLogin: "success", recordExit: "3"), "unavailable", "passed, but evaluation history wasn't recorded", 1 },
        { "scheduled pass, record sign-in failed", Env("workflow_dispatch", reportsValid: "true", recordLogin: "failure", recordExit: ""), "unavailable", "passed, but evaluation history wasn't recorded", 1 },
        { "scheduled pass, recorded", Env("schedule", reportsValid: "true", recordLogin: "success", recordExit: "0"), "passed", "passed", 0 },
        { "scheduled exit 3, nothing to record", Env("schedule", investigation: "3", reportsValid: "false"), "unavailable", "dev environment unavailable", 1 },
        { "pull request pass, no record outputs", Env(reportsValid: "true"), "passed", "passed", 0 },
    };

    [Theory]
    [MemberData(nameof(Verdicts))]
    public void The_verdict_follows_the_precedence_table(string row, Dictionary<string, string> env, string status, string message, int exitCode)
    {
        var (outputs, code) = RunVerdict(env);

        Assert.True(status == outputs.GetValueOrDefault("status"), $"{row}: status {outputs.GetValueOrDefault("status")}");
        Assert.Equal(message, outputs.GetValueOrDefault("message"));
        Assert.Equal(exitCode, code);
    }

    // A gate failure leaves retrieval worth running; input that won't load or an environment that's down won't improve.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(134, false)]
    public void Retrieval_runs_only_after_an_investigation_result(int investigationExit, bool retrievalRuns)
    {
        var (outputs, commands, exitCode, _) = RunEvaluations("evaluate", investigationExit);

        Assert.Equal(0, exitCode);
        Assert.Equal(investigationExit.ToString(CultureInfo.InvariantCulture), outputs["investigation-exit"]);
        Assert.Equal(retrievalRuns ? ["investigation-eval", "retrieval-eval"] : ["investigation-eval"], commands);
        Assert.Equal(retrievalRuns ? "0" : null, outputs.GetValueOrDefault("retrieval-exit"));
    }

    // Its job runs nothing else, so every exit is passed on for the verdict to read.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(134)]
    public void The_safety_job_runs_only_the_safety_evaluation_and_reports_its_exit(int safetyExit)
    {
        var (outputs, commands, exitCode, _) = RunEvaluations("evaluate-safety", safetyExit: safetyExit);

        Assert.Equal(0, exitCode);
        Assert.Equal(["safety-eval"], commands);
        Assert.Equal(safetyExit.ToString(CultureInfo.InvariantCulture), outputs["safety-exit"]);
    }

    // Its hard gates fail on any one run, so a pull request runs it once; the scheduled run keeps three for the history.
    // The investigations keep three on every trigger: their thresholds gate the median of three.
    [Theory]
    [InlineData("pull_request", "1", "3")]
    [InlineData("schedule", "3", "3")]
    [InlineData("workflow_dispatch", "3", "3")]
    public void A_pull_request_runs_the_safety_evaluation_once(string eventName, string safetyRuns, string investigationRuns)
    {
        Assert.Equal([safetyRuns], RunEvaluations("evaluate-safety", eventName: eventName).Runs);
        Assert.Equal([investigationRuns], RunEvaluations("evaluate", eventName: eventName).Runs);
    }

    [Theory]
    [InlineData(2, 0, 0, "invalid-input")]
    [InlineData(3, 0, 0, "unavailable")]
    [InlineData(0, 2, 0, "invalid-input")]
    [InlineData(0, 3, 0, "unavailable")]
    [InlineData(1, 0, 3, "unavailable")]
    [InlineData(0, 0, 2, "invalid-input")]
    public void The_verdict_names_why_the_evaluations_stopped(int investigationExit, int retrievalExit, int safetyExit, string status)
    {
        var (outputs, _, _, _) = RunEvaluations("evaluate", investigationExit, retrievalExit);

        // The safety job passes its exit on unchanged, as its own test shows.
        var (verdict, code) = RunVerdict(Env(
            investigation: outputs["investigation-exit"],
            retrieval: outputs.GetValueOrDefault("retrieval-exit", string.Empty),
            safety: safetyExit.ToString(CultureInfo.InvariantCulture),
            reportsValid: "false"));

        Assert.Equal(status, verdict["status"]);
        Assert.Equal(1, code);
    }

    [Fact]
    public void History_is_recorded_for_gate_failures_but_not_for_invalid_reports()
    {
        var recordIf = Scalar(Job("record"), "if")!;

        Assert.Contains("needs.evaluate.outputs.reports-valid == 'true'", recordIf, StringComparison.Ordinal);
        Assert.Contains("needs.evaluate-safety.outputs.reports-valid == 'true'", recordIf, StringComparison.Ordinal);
        foreach (var job in AttemptJobs)
        {
            Assert.Equal("${{ github.run_attempt }}", Scalar((YamlMappingNode)Job(job)["outputs"], "attempt"));
            Assert.Contains($"needs.{job}.outputs.attempt == github.run_attempt", recordIf, StringComparison.Ordinal);
            Assert.Contains($"needs.{job}.outputs.attempt }}}}", Serialize(Step("verdict", "verdict")["env"]), StringComparison.Ordinal);
        }

        Assert.Contains("github.event_name != 'pull_request'", recordIf, StringComparison.Ordinal);
        Assert.DoesNotContain("exit", recordIf, StringComparison.Ordinal);
        Assert.Equal(
            "${{ needs.evaluate.outputs.reports-valid == 'true' && needs.evaluate-safety.outputs.reports-valid == 'true' }}",
            Scalar((YamlMappingNode)Step("verdict", "verdict")["env"], "REPORTS_VALID"));
    }

    public static TheoryData<string, string, string, bool> InvestigationReports() => new()
    {
        { "0", "0", "investigations retrieval", true },
        { "1", "0", "investigations retrieval", true },
        { "0", "1", "investigations retrieval", true },
        { "0", "2", "investigations retrieval", false },
        { "3", "", "investigations", false },
        { "0", "", "investigations retrieval", false },
        { "0", "0", "investigations", false },
    };

    // A report is worth recording only when its command scored it: an exit of 0 or 1, and the file.
    [Theory]
    [MemberData(nameof(InvestigationReports))]
    public void Investigation_reports_are_valid_only_when_both_exist_and_scored(string investigation, string retrieval, string files, bool valid)
    {
        var outputs = RunReports("evaluate", files, new(StringComparer.Ordinal) { ["INVESTIGATION_EXIT"] = investigation, ["RETRIEVAL_EXIT"] = retrieval });

        Assert.Equal(valid ? "true" : "false", outputs.GetValueOrDefault("reports-valid"));
    }

    [Theory]
    [InlineData("0", "safety", true)]
    [InlineData("1", "safety", true)]
    [InlineData("2", "safety", false)]
    [InlineData("3", "", false)]
    [InlineData("", "", false)]
    [InlineData("0", "", false)]
    public void The_safety_report_is_valid_only_when_it_exists_and_scored(string safety, string files, bool valid)
    {
        var outputs = RunReports("evaluate-safety", files, new(StringComparer.Ordinal) { ["SAFETY_EXIT"] = safety });

        Assert.Equal(valid ? "true" : "false", outputs.GetValueOrDefault("reports-valid"));
    }

    [Fact]
    public void Safety_eval_runs_without_a_thresholds_file()
    {
        var safety = Scalar(Step("evaluate-safety", "evaluate"), "run")!;

        Assert.Contains("-- safety-eval", safety, StringComparison.Ordinal);
        Assert.DoesNotContain("--thresholds", safety, StringComparison.Ordinal);
        Assert.Contains("--dataset eval/datasets/safety.json --runs \"$runs\"", safety, StringComparison.Ordinal);
        Assert.Contains("--index \"$INDEX\"", safety, StringComparison.Ordinal);
        Assert.Contains("--out \"$RUNNER_TEMP/eval/safety.json\"", safety, StringComparison.Ordinal);
        Assert.Equal("${{ steps.evaluate.outputs.safety-exit }}", Scalar((YamlMappingNode)Job("evaluate-safety")["outputs"], "safety-exit"));
    }

    // Each evaluate job uploads its own artifact, and the record job downloads them all into one folder.
    [Fact]
    public void Every_report_is_uploaded_and_recorded()
    {
        var uploads = EvaluateJobs
            .Select(job => (YamlMappingNode)Steps(Job(job)).Single(s => Uses(s, "actions/upload-artifact"))["with"])
            .ToList();
        var download = (YamlMappingNode)Steps(Job("record")).Single(s => Uses(s, "actions/download-artifact"))["with"];
        var record = Scalar(Step("record", "record"), "run")!;

        Assert.Equal("ai-evaluation-*", Scalar(download, "pattern"));
        Assert.Equal("true", Scalar(download, "merge-multiple"));
        Assert.Equal("${{ runner.temp }}/eval", Scalar(download, "path"));
        Assert.All(uploads, u => Assert.StartsWith("ai-evaluation-", Scalar(u, "name"), StringComparison.Ordinal));
        foreach (var report in ReportNames)
        {
            Assert.Single(uploads, u => Scalar(u, "path")!.Contains($"${{{{ runner.temp }}}}/eval/{report}.json", StringComparison.Ordinal));
            Assert.Contains($"\"$RUNNER_TEMP/eval/{report}.json\"", record, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_action_is_pinned_by_commit()
    {
        var uses = AllJobs().SelectMany(j => Steps(j.Job)).Select(s => Scalar(s, "uses")).OfType<string>().Where(u => !u.StartsWith("./", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, u => Assert.Matches(@"^[\w./-]+@[0-9a-f]{40}$", u));
    }

    private static Dictionary<string, string> Env(
        string eventName = "pull_request",
        string required = "true",
        string requested = "true",
        string trusted = "true",
        string violation = "",
        string reason = "required: AI behaviour changed",
        string publish = "none",
        string login = "success",
        string safetyLogin = "success",
        string runAttempt = "1",
        string publishAttempt = "1",
        string evaluateAttempt = "1",
        string safetyAttempt = "1",
        string investigation = "0",
        string retrieval = "0",
        string safety = "0",
        string reportsValid = "true",
        string recordLogin = "",
        string recordExit = "") => new(StringComparer.Ordinal)
        {
            ["EVENT_NAME"] = eventName,
            ["REQUIRED"] = required,
            ["REQUESTED"] = requested,
            ["TRUSTED"] = trusted,
            ["VIOLATION"] = violation,
            ["REASON"] = reason,
            ["PUBLISH_FAILURE"] = publish,
            ["EVALUATE_LOGIN"] = login,
            ["SAFETY_LOGIN"] = safetyLogin,
            ["RUN_ATTEMPT"] = runAttempt,
            ["PUBLISH_ATTEMPT"] = publishAttempt,
            ["EVALUATE_ATTEMPT"] = evaluateAttempt,
            ["SAFETY_ATTEMPT"] = safetyAttempt,
            ["INVESTIGATION_EXIT"] = investigation,
            ["RETRIEVAL_EXIT"] = retrieval,
            ["SAFETY_EXIT"] = safety,
            ["REPORTS_VALID"] = reportsValid,
            ["RECORD_LOGIN"] = recordLogin,
            ["RECORD_EXIT"] = recordExit,
        };

    // The job's own script, with a stand-in dotnet that records which evaluation was started, with how many runs, and exits as told.
    private static (Dictionary<string, string> Outputs, List<string> Commands, int ExitCode, List<string> Runs) RunEvaluations(
        string job, int investigationExit = 0, int retrievalExit = 0, int safetyExit = 0, string eventName = "pull_request")
    {
        var directory = Directory.CreateTempSubdirectory("evaluations-");
        var log = Path.Combine(directory.FullName, "commands");
        var runsLog = Path.Combine(directory.FullName, "runs");
        var dotnet = Path.Combine(directory.FullName, "dotnet");
        File.WriteAllText(dotnet, """
            #!/usr/bin/env bash
            previous=
            for argument in "$@"; do
              [[ "$previous" == --runs ]] && echo "$argument" >> "$RUNS_LOG"
              previous="$argument"
            done
            for argument in "$@"; do
              case "$argument" in
                investigation-eval) echo investigation-eval >> "$COMMAND_LOG"; exit "$INVESTIGATION_CODE" ;;
                retrieval-eval) echo retrieval-eval >> "$COMMAND_LOG"; exit "$RETRIEVAL_CODE" ;;
                safety-eval) echo safety-eval >> "$COMMAND_LOG"; exit "$SAFETY_CODE" ;;
              esac
            done
            exit 99
            """.ReplaceLineEndings("\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var (outputs, exitCode) = RunStep(Step(job, "evaluate"), directory, new(StringComparer.Ordinal)
        {
            ["PATH"] = directory.FullName + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["COMMAND_LOG"] = log,
            ["RUNS_LOG"] = runsLog,
            ["INVESTIGATION_CODE"] = investigationExit.ToString(CultureInfo.InvariantCulture),
            ["RETRIEVAL_CODE"] = retrievalExit.ToString(CultureInfo.InvariantCulture),
            ["SAFETY_CODE"] = safetyExit.ToString(CultureInfo.InvariantCulture),
            ["EVENT_NAME"] = eventName,
            ["INDEX"] = "evaluation",
            ["RUNNER_TEMP"] = directory.FullName,
        });

        return (outputs, [.. File.ReadAllLines(log)], exitCode, File.Exists(runsLog) ? [.. File.ReadAllLines(runsLog)] : []);
    }

    // The job's report check, run over a folder holding the named report files.
    private static Dictionary<string, string> RunReports(string job, string files, Dictionary<string, string> env)
    {
        var directory = Directory.CreateTempSubdirectory("reports-");
        Directory.CreateDirectory(Path.Combine(directory.FullName, "eval"));
        foreach (var name in files.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            File.WriteAllText(Path.Combine(directory.FullName, "eval", $"{name}.json"), "{}");
        }

        env["RUNNER_TEMP"] = directory.FullName.Replace('\\', '/');
        return RunStep(Step(job, "reports"), directory, env).Outputs;
    }

    private static (Dictionary<string, string> Outputs, int ExitCode) RunVerdict(Dictionary<string, string> env)
    {
        var step = Step("verdict", "verdict");
        var declared = ((YamlMappingNode)step["env"]).Children.Keys.Select(k => ((YamlScalarNode)k).Value!).Order(StringComparer.Ordinal);
        Assert.Equal(env.Keys.Order(StringComparer.Ordinal), declared);
        return RunStep(step, Directory.CreateTempSubdirectory("verdict-"), env);
    }

    private static (Dictionary<string, string> Outputs, int ExitCode) RunStep(YamlMappingNode step, DirectoryInfo directory, Dictionary<string, string> env)
    {
        var script = Path.Combine(directory.FullName, "step.sh");
        File.WriteAllText(script, Scalar(step, "run")!.ReplaceLineEndings("\n"));
        var output = Path.Combine(directory.FullName, "output");
        var start = new ProcessStartInfo(Bash(), ["-e", script]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var (key, value) in env)
        {
            start.Environment[key] = value;
        }

        start.Environment["GITHUB_OUTPUT"] = output;
        start.Environment["GITHUB_STEP_SUMMARY"] = Path.Combine(directory.FullName, "summary");
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();

        var outputs = File.Exists(output)
            ? File.ReadAllLines(output).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal)
            : [];
        return (outputs, process.ExitCode);
    }

    // On Windows, Git's bash; System32's bash is WSL, which may not be installed.
    private static string Bash()
    {
        var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        return OperatingSystem.IsWindows() && File.Exists(git) ? git : "bash";
    }

    private static string Serialize(YamlNode node)
    {
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(node)).Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    [GeneratedRegex(@"needs\.(?<job>[\w-]+)\.outputs\.(?<output>[\w-]+)")]
    private static partial Regex NeedsOutput();
}
