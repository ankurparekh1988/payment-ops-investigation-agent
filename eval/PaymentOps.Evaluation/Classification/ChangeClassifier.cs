using System.Text.RegularExpressions;

namespace PaymentOps.Evaluation.Classification;

/// <param name="Required">Whether the change must pass the evaluation gate.</param>
/// <param name="Trusted">False for a fork or a bot: neither may reach the evaluation environments.</param>
/// <param name="Violation">Why the change can't be evaluated as it stands; null when it can.</param>
/// <param name="IndexSwitch">The change points the release at a different live index, so that index is evaluated as it is.</param>
internal sealed record ChangeClassification(bool Required, bool Trusted, string? Violation, bool IndexSwitch, string Reason);

/// <summary>
/// Decides what a pull request needs from the evaluation gate, from its changed paths. Behaviour and gate policy
/// change in separate pull requests, so a change can't move the agent and the bar it's judged by together.
/// </summary>
internal static class ChangeClassifier
{
    public const string MixedChange = "change gate policy and AI behaviour in separate pull requests";
    public const string MissingAdr = "a change to the thresholds or the evaluation datasets needs an ADR under docs/adr/ in the same pull request";
    public const string IndexSwitchWithKnowledge = "switch the knowledge index and change the knowledge corpus or how it's built in separate pull requests";

    public static readonly string[] BehaviourPaths =
    [
        "ai/**", "src/PaymentOps.Agent/**", "src/PaymentOps.Tools/**", "src/PaymentOps.Knowledge/**", "src/PaymentOps.Data.Demo/**",
        "src/PaymentOps.Platform/**", "data/knowledge/**", "data/datasets/**", "tools/PaymentOps.CorpusBuild/**",
        "tools/PaymentOps.KnowledgePublish/**", "Directory.Packages.props", "global.json", "src/**/*.csproj",
    ];

    public static readonly string[] GatePolicyPaths = ["eval/**", ".github/workflows/ai-evaluation.yml", "docs/adr/0012-*"];

    private static readonly Regex[] Behaviour = Globs(BehaviourPaths);
    private static readonly Regex[] GatePolicy = Globs(GatePolicyPaths);

    // Labels live in the datasets, so a label change is a dataset change.
    private static readonly Regex[] AdrRequired = Globs(["eval/thresholds.json", "eval/datasets/**"]);
    private static readonly Regex[] Adr = Globs(["docs/adr/*.md"]);
    // What decides the published corpus; a switched index is evaluated as CD built it, so neither may change with the switch.
    private static readonly Regex[] Corpus = Globs(["data/knowledge/**", "tools/PaymentOps.CorpusBuild/**"]);

    public static ChangeClassification Classify(IReadOnlyList<string> changedPaths, string baseIndex, string headIndex, bool sameRepository, bool actorIsBot)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);
        var trusted = sameRepository && !actorIsBot;
        var indexSwitch = !string.Equals(baseIndex, headIndex, StringComparison.Ordinal);
        var behaviour = changedPaths.Any(IsBehaviour);
        var policy = changedPaths.Any(IsGatePolicy);

        var violation =
            behaviour && policy ? MixedChange
            : changedPaths.Any(p => Matches(AdrRequired, p)) && !changedPaths.Any(p => Matches(Adr, p)) ? MissingAdr
            : indexSwitch && changedPaths.Any(p => Matches(Corpus, p)) ? IndexSwitchWithKnowledge
            : null;

        var reason = violation
            ?? (behaviour ? "required: AI behaviour changed"
            : policy ? "required: gate policy changed"
            : "not required: no AI behaviour or gate policy changed");
        return new ChangeClassification(behaviour || policy, trusted, violation, indexSwitch, reason);
    }

    public static bool IsBehaviour(string path) => Matches(Behaviour, path);

    public static bool IsGatePolicy(string path) => Matches(GatePolicy, path);

    private static bool Matches(Regex[] globs, string path) => globs.Any(g => g.IsMatch(path));

    private static Regex[] Globs(string[] patterns) => [.. patterns.Select(Glob)];

    // "**/" is any number of whole folders, a trailing "**" is anything below, and "*" stays within one name.
    private static Regex Glob(string pattern)
    {
        var regex = Regex.Escape(pattern)
            .Replace(@"\*\*/", "(?:[^/]+/)*", StringComparison.Ordinal)
            .Replace(@"\*\*", ".+", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal);
        return new Regex($"^{regex}$", RegexOptions.CultureInvariant);
    }
}
