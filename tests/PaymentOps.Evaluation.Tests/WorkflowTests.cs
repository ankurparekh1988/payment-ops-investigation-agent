using System.Diagnostics;
using System.Text.RegularExpressions;

using PaymentOps.Tests.Shared;

using YamlDotNet.RepresentationModel;

namespace PaymentOps.Evaluation.Tests;

/// <summary>The AI evaluation workflow's shape and its verdict, read from the YAML that runs.</summary>
public sealed partial class WorkflowTests
{
    private static readonly string WorkflowPath = Path.Combine(RepositoryRoot.FullPath, ".github", "workflows", "ai-evaluation.yml");

    private static readonly YamlMappingNode Workflow = Load();

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
        Assert.Equal("0 6 * * 1", Scalar((YamlMappingNode)((YamlSequenceNode)on["schedule"]).Children[0], "cron"));
    }

    [Fact]
    public void One_concurrency_group_never_cancels_a_run_in_progress()
    {
        var concurrency = (YamlMappingNode)Workflow["concurrency"];

        Assert.StartsWith("ai-eval-", Scalar(concurrency, "group"), StringComparison.Ordinal);
        Assert.Equal("false", Scalar(concurrency, "cancel-in-progress"));
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
        Assert.Contains("[[ -f base/eval/PaymentOps.Evaluation/Commands/ClassifyChangeCommand.cs ]]", check, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot.FullPath, "eval", "PaymentOps.Evaluation", "Commands", "ClassifyChangeCommand.cs")));
    }

    [Fact]
    public void The_classifier_uses_the_base_toolchain()
    {
        var setups = AllJobs().SelectMany(j => Steps(j.Job).Select(s => (j.Id, Step: s)))
            .Where(s => (Scalar(s.Step, "uses") ?? string.Empty).StartsWith("actions/setup-dotnet@", StringComparison.Ordinal))
            .ToList();

        var classify = Assert.Single(setups, s => s.Id == "classify");
        Assert.Equal("base/global.json", Scalar((YamlMappingNode)classify.Step["with"], "global-json-file"));
        Assert.Contains("dotnet run --project base/eval/PaymentOps.Evaluation", Scalar(Step("classify", "classify"), "run"), StringComparison.Ordinal);
        Assert.All(setups.Where(s => s.Id != "classify"), s => Assert.Equal("global.json", Scalar((YamlMappingNode)s.Step["with"], "global-json-file")));
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
        { "2 violation", Env(violation: "change gate policy and AI behaviour in separate pull requests"), "policy-violation", "change gate policy and AI behaviour in separate pull requests", 1 },
        { "3 untrusted", Env(trusted: "false"), "untrusted", "reproduce this change on a branch in this repository", 1 },
        { "4 not required", Env(required: "false", reason: "not required: the gate is introduced by this change"), "not-required", "not required: the gate is introduced by this change", 0 },
        { "4 untrusted but not required", Env(required: "false", trusted: "false", reason: "not required: no AI behaviour or gate policy changed"), "not-required", "not required: no AI behaviour or gate policy changed", 0 },
        { "5 scope violation", Env(publish: "scope-violation", investigation: "", retrieval: "", login: ""), "scope-violation", "the evaluation publisher can write the live index; fix its role before evaluating", 1 },
        { "6 publish failed", Env(publish: "publish-failed", investigation: "", retrieval: "", login: ""), "invalid-input", "invalid evaluation input", 1 },
        { "6 exit 2", Env(investigation: "2"), "invalid-input", "invalid evaluation input", 1 },
        { "7 publish sign-in", Env(publish: "login", investigation: "", retrieval: "", login: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 publish unavailable", Env(publish: "unavailable", investigation: "", retrieval: "", login: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 evaluate sign-in", Env(login: "failure", investigation: "", retrieval: ""), "unavailable", "dev environment unavailable", 1 },
        { "7 exit 3", Env(retrieval: "3"), "unavailable", "dev environment unavailable", 1 },
        { "8 exit missing", Env(retrieval: ""), "error", "the evaluation workflow didn't complete", 1 },
        { "9 gate failed", Env(investigation: "1"), "failed", "gate failed", 1 },
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

    [Fact]
    public void History_is_recorded_for_gate_failures_but_not_for_invalid_reports()
    {
        var recordIf = Scalar(Job("record"), "if")!;
        var reports = Scalar(Step("evaluate", "reports"), "run")!;

        Assert.Contains("needs.evaluate.outputs.reports-valid == 'true'", recordIf, StringComparison.Ordinal);
        Assert.Contains("github.event_name != 'pull_request'", recordIf, StringComparison.Ordinal);
        Assert.DoesNotContain("exit", recordIf, StringComparison.Ordinal);
        Assert.Contains("0/0|0/1|1/0|1/1)", reports, StringComparison.Ordinal);
        Assert.Contains("-f \"$RUNNER_TEMP/eval/investigations.json\"", reports, StringComparison.Ordinal);
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
        string trusted = "true",
        string violation = "",
        string reason = "required: AI behaviour changed",
        string publish = "none",
        string login = "success",
        string investigation = "0",
        string retrieval = "0",
        string reportsValid = "true",
        string recordLogin = "",
        string recordExit = "") => new(StringComparer.Ordinal)
        {
            ["EVENT_NAME"] = eventName,
            ["REQUIRED"] = required,
            ["TRUSTED"] = trusted,
            ["VIOLATION"] = violation,
            ["REASON"] = reason,
            ["PUBLISH_FAILURE"] = publish,
            ["EVALUATE_LOGIN"] = login,
            ["INVESTIGATION_EXIT"] = investigation,
            ["RETRIEVAL_EXIT"] = retrieval,
            ["REPORTS_VALID"] = reportsValid,
            ["RECORD_LOGIN"] = recordLogin,
            ["RECORD_EXIT"] = recordExit,
        };

    private static (Dictionary<string, string> Outputs, int ExitCode) RunVerdict(Dictionary<string, string> env)
    {
        var step = Step("verdict", "verdict");
        var declared = ((YamlMappingNode)step["env"]).Children.Keys.Select(k => ((YamlScalarNode)k).Value!).Order(StringComparer.Ordinal);
        Assert.Equal(env.Keys.Order(StringComparer.Ordinal), declared);

        var directory = Directory.CreateTempSubdirectory("verdict-");
        var script = Path.Combine(directory.FullName, "verdict.sh");
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
