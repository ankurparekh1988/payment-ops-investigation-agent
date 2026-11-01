using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Agent;
using PaymentOps.Evaluation.Cases;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Running;
using PaymentOps.Evaluation.Safety;
using PaymentOps.Evaluation.Scoring;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

namespace PaymentOps.Evaluation.Commands;

internal sealed record SafetyEvalSettings(
    InvestigationEndpoints Endpoints,
    Uri ContentSafetyEndpoint,
    string Sources,
    string Dataset,
    int Runs,
    string Pricing,
    string? Thresholds,
    string Out,
    string Summary,
    AiRelease Release);

/// <summary>
/// safety-eval: the safety cases N times against the agent, with each case's planted text beneath its tools,
/// signed in as whoever ran <c>az login</c>. Report-only without --thresholds: the hard gates still decide.
/// There's no judge, and nothing is written but the report and the summary.
/// </summary>
internal static class SafetyEvalCommand
{
    public const string Usage =
        "safety-eval --foundry <url> --content-safety <url> --search <url> --scope <token scope> --ai <dir> --sources <dir> --dataset <file> " +
        "--runs <n> --pricing <file> [--thresholds <file>] [--index live|evaluation] --out <file> --summary <file>";

    private static readonly string[] Valued = ["foundry", "content-safety", "search", "scope", "ai", "sources", "dataset", "runs", "pricing", "thresholds", "index", "out", "summary"];

    public static Task<int> RunAsync(string[] args) => RunAsync(args, null, TimeSpan.FromMinutes(1));

    /// <param name="runners">Tests only: the system under evaluation, given each case's overlay; by default the agent, live.</param>
    /// <param name="retryDelay">How long to wait before repeating an invalid run.</param>
    public static Task<int> RunAsync(
        string[] args,
        Func<AiRelease, SafetyEvalSettings, Func<InvestigationCase, Action<IServiceCollection>?>, ICaseRunner>? runners,
        TimeSpan retryDelay) => ExitCodes.GuardAsync("Safety evaluation", Usage, async () =>
    {
        var settings = Parse(args);
        var release = settings.Release;
        var corpus = IndexedCorpus.FromSourceDirectory(settings.Sources);
        var cases = SafetyDataset.Load(settings.Dataset, corpus);
        var prices = PriceList.Load(settings.Pricing);
        var thresholds = settings.Thresholds is { } path ? SafetyThresholds.Load(path) : null;
        if (thresholds is not null)
        {
            SafetyGate.Require(thresholds, SafetyMetrics.Opportunities(cases.Cases));
        }

        var byId = cases.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        Func<InvestigationCase, Action<IServiceCollection>?> overlay = c => Poisoning.For(byId[c.Id], cases.PoisonedSections.GetValueOrDefault(c.Id));
        var runner = runners?.Invoke(release, settings, overlay) ?? Runner(release, settings, overlay);
        var context = new ReportContext(
            EvaluationReport.SafetySystem,
            RepositoryCommit.Current(),
            [.. release.Prompts.Values.Select(p => new PromptInfo(p.Id, p.Version))],
            ModelInfo.From(release, release.ActiveChatDeployment),
            null,
            null,
            DateTimeOffset.UtcNow,
            release.KnowledgeIndex);

        var runs = await RunPlan.RunAsync(runner, [.. cases.Cases.Select(c => c.ToInvestigationCase())], settings.Runs, retryDelay, CancellationToken.None);
        var classification = corpus.ToDictionary(s => s.Id, s => s.Classification, StringComparer.Ordinal);
        var report = EvaluationReport.ForSafety(context, cases.Cases, classification, runs, prices, thresholds, settings.Thresholds);
        await File.WriteAllTextAsync(settings.Out, report.ToJson());
        await File.AppendAllTextAsync(settings.Summary, ReportMarkdown.Safety(report));
        Console.WriteLine($"safety: gate {report.Gate.Verdict} over {runs.Count} run(s).");
        if (report.Gate.Verdict == GateReport.Failed)
        {
            GitHubAnnotation.Errors("Safety evaluation", report.Gate.Failures);
            return ExitCodes.Failed;
        }

        return ExitCodes.Passed;
    });

    private static SafetyEvalSettings Parse(string[] args)
    {
        var options = Options.Parse(args, Valued, []);
        return new SafetyEvalSettings(
            new InvestigationEndpoints(options.Https("search"), options.Https("foundry"), options.One("scope")),
            options.Https("content-safety"),
            options.One("sources"),
            options.One("dataset"),
            options.Positive("runs"),
            options.One("pricing"),
            options.Optional("thresholds"),
            options.OutputFile("out"),
            options.OutputFile("summary"),
            options.Release());
    }

    // In the pipeline, azure/login signs the CLI in through OIDC; locally it's the developer's az login.
    private static AgentCaseRunner Runner(AiRelease release, SafetyEvalSettings settings, Func<InvestigationCase, Action<IServiceCollection>?> overlay)
    {
        TokenCredential credential = new CachedTokenCredential(new AzureCliCredential());
        return new AgentCaseRunner(release, settings.Endpoints, credential, configure: EvaluationServices.AppConfiguration(settings.ContentSafetyEndpoint, settings.Endpoints.FoundryTokenScope), overlay: overlay);
    }
}
