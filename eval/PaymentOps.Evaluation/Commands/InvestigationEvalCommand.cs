using System.Diagnostics;

using Azure.Core;
using Azure.Identity;

using PaymentOps.Agent;
using PaymentOps.Agent.Model;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Challenger;
using PaymentOps.Evaluation.Judging;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

namespace PaymentOps.Evaluation.Commands;

internal sealed record InvestigationEvalSettings(
    InvestigationEndpoints Endpoints,
    string Sources,
    IReadOnlyList<string> Datasets,
    int Runs,
    string System,
    string Pricing,
    string? Thresholds,
    bool Judge,
    string Out,
    string Summary,
    AiRelease Release);

/// <summary>
/// investigation-eval: the labelled cases, N times, against the deployed model and index, signed in as whoever
/// ran <c>az login</c>. Calls the model and queries Search; writes only the report and the summary.
/// </summary>
internal static class InvestigationEvalCommand
{
    public const string Usage =
        "investigation-eval --foundry <url> --search <url> --scope <token scope> --ai <dir> --sources <dir> --dataset <file> [--dataset <file>] " +
        "--runs <n> --system agent|fixed-workflow --pricing <file> [--thresholds <file>] [--index live|evaluation] [--no-judge] --out <file> --summary <file>";

    private static readonly string[] Valued = ["foundry", "search", "scope", "ai", "sources", "dataset", "runs", "system", "pricing", "thresholds", "index", "out", "summary"];
    private static readonly string[] Flags = ["no-judge"];

    public static Task<int> RunAsync(string[] args) => RunAsync(args, null, null, TimeSpan.FromMinutes(1));

    /// <param name="runners">Tests only: the system under evaluation; by default the agent or the fixed workflow, live.</param>
    /// <param name="judges">Tests only: the judge; by default the release's pinned judge deployment.</param>
    /// <param name="retryDelay">How long to wait before repeating an invalid run.</param>
    public static Task<int> RunAsync(
        string[] args,
        Func<AiRelease, InvestigationEvalSettings, ICaseRunner>? runners,
        Func<AiRelease, Judge?>? judges,
        TimeSpan retryDelay) => ExitCodes.GuardAsync("Investigation evaluation", Usage, async () =>
    {
        var settings = Parse(args);
        var release = settings.Release;
        var cases = InvestigationDataset.Load(settings.Datasets, IndexedCorpus.FromSourceDirectory(settings.Sources), []);
        var prices = PriceList.Load(settings.Pricing);
        var thresholds = settings.Thresholds is { } path ? Thresholds.Load(path) : null;
        if (thresholds is not null)
        {
            Gate.Require(thresholds, EvaluationReport.ApplicableN(Opportunities.Count(cases.Cases)));
        }

        // One sign-in for the whole evaluation, so token fetches stay out of every case's timings.
        var credential = new CachedTokenCredential(new AzureCliCredential());
        var runner = runners?.Invoke(release, settings) ?? Runner(release, settings, credential);
        var judge = judges is not null ? judges(release) : settings.Judge ? LiveJudge(release, settings, credential) : null;
        var context = new ReportContext(
            settings.System,
            RepositoryCommit.Current(),
            Prompts(release, settings.System),
            ModelInfo.From(release, release.ActiveChatDeployment),
            judge is null ? null : ModelInfo.From(release, release.JudgeDeployment),
            judge is null ? null : Judge.Prompt.Version,
            DateTimeOffset.UtcNow,
            release.KnowledgeIndex);

        var runs = await RunPlan.RunAsync(runner, cases.Cases, settings.Runs, retryDelay, CancellationToken.None);
        var byId = cases.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var judged = new List<IReadOnlyDictionary<string, JudgeScore>>();
        foreach (var run in runs)
        {
            var scores = new Dictionary<string, JudgeScore>(StringComparer.Ordinal);
            if (judge is not null)
            {
                foreach (var result in run.Results)
                {
                    scores[result.CaseId] = await judge.ScoreAsync(byId[result.CaseId], result, CancellationToken.None);
                }
            }

            judged.Add(scores);
        }

        var report = EvaluationReport.ForInvestigations(context, cases, runs, judged, prices, thresholds, settings.Thresholds);
        await File.WriteAllTextAsync(settings.Out, report.ToJson());
        await File.AppendAllTextAsync(settings.Summary, ReportMarkdown.Investigations(report));
        Console.WriteLine($"{settings.System}: gate {report.Gate.Verdict} over {runs.Count} run(s).");
        if (report.Gate.Verdict == GateReport.Failed)
        {
            GitHubAnnotation.Errors("Investigation evaluation", report.Gate.Failures);
            return ExitCodes.Failed;
        }

        return ExitCodes.Passed;
    });

    private static InvestigationEvalSettings Parse(string[] args)
    {
        var options = Options.Parse(args, Valued, Flags);
        var system = options.One("system");
        if (system is not (EvaluationReport.AgentSystem or EvaluationReport.FixedWorkflowSystem))
        {
            throw new UsageException("--system must be agent or fixed-workflow.");
        }

        return new InvestigationEvalSettings(
            new InvestigationEndpoints(options.Https("search"), options.Https("foundry"), options.One("scope")),
            options.One("sources"),
            options.All("dataset"),
            options.Positive("runs"),
            system,
            options.One("pricing"),
            options.Optional("thresholds"),
            !options.Flag("no-judge"),
            options.OutputFile("out"),
            options.OutputFile("summary"),
            options.Release());
    }

    // In the pipeline, azure/login signs the CLI in through OIDC; locally it's the developer's az login.
    private static ICaseRunner Runner(AiRelease release, InvestigationEvalSettings settings, TokenCredential credential) => settings.System == EvaluationReport.AgentSystem
        ? new AgentCaseRunner(release, settings.Endpoints, credential)
        : new FixedWorkflowCaseRunner(release, settings.Endpoints, credential);

    private static Judge LiveJudge(AiRelease release, InvestigationEvalSettings settings, TokenCredential credential) =>
        new(ModelClient.Create(settings.Endpoints.FoundryEndpoint, settings.Endpoints.FoundryTokenScope, credential, release.JudgeDeployment));

    private static IReadOnlyList<PromptInfo> Prompts(AiRelease release, string system) => system == EvaluationReport.AgentSystem
        ? [.. release.Prompts.Values.Select(p => new PromptInfo(p.Id, p.Version))]
        : [new(FixedWorkflow.Prompt.Id, FixedWorkflow.Prompt.Version), new(ScopeExtraction.Prompt.Id, ScopeExtraction.Prompt.Version)];
}

/// <summary>
/// The commit being evaluated: the workflow's, or the working tree's HEAD, marked dirty when tracked files have
/// uncommitted changes, so a report never claims code that wasn't what ran.
/// </summary>
internal static class RepositoryCommit
{
    public const string Unknown = "unknown";
    public const string DirtySuffix = "-dirty";

    public static string Current()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_SHA") is { Length: > 0 } sha)
        {
            return sha;
        }

        var head = Git("rev-parse HEAD");
        return head is null ? Unknown : Git("status --porcelain --untracked-files=no") is "" ? head : head + DirtySuffix;
    }

    /// <summary>A full commit sha with nothing uncommitted: the only kind a baseline may come from.</summary>
    public static bool IsClean(string commit) => commit.Length == 40 && commit.All(Uri.IsHexDigit);

    private static string? Git(string arguments)
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", arguments) { RedirectStandardOutput = true, UseShellExecute = false });
            var output = git?.StandardOutput.ReadToEnd().Trim();
            git?.WaitForExit();
            return git?.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
