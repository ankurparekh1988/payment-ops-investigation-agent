using PaymentOps.Evaluation.Classification;

using YamlDotNet.RepresentationModel;

namespace PaymentOps.Evaluation.Commands;

/// <summary>
/// classify-change: what the evaluation gate needs from a pull request, written as step outputs. CI runs it from the
/// base commit, so a pull request can't change the rules it's classified by. Reads files only.
/// </summary>
internal static class ClassifyChangeCommand
{
    public const string Usage =
        "classify-change --changed <file> --base-manifest <file> --head-manifest <file> --same-repository true|false --actor-type <User|Bot> --github-output <file>";

    private static readonly string[] Valued = ["changed", "base-manifest", "head-manifest", "same-repository", "actor-type", "github-output"];

    public static Task<int> RunAsync(string[] args) => ExitCodes.GuardAsync("Change classification", Usage, async () =>
    {
        var options = Options.Parse(args, Valued, []);
        var sameRepository = options.One("same-repository") switch
        {
            "true" => true,
            "false" => false,
            _ => throw new UsageException("--same-repository must be true or false."),
        };
        var actorIsBot = options.One("actor-type") switch
        {
            "Bot" => true,
            "User" => false,
            _ => throw new UsageException("--actor-type must be User or Bot."),
        };

        var changed = (await File.ReadAllLinesAsync(options.One("changed"))).Where(line => line.Length > 0).ToList();
        var result = ChangeClassifier.Classify(changed, LiveIndex(options.One("base-manifest")), LiveIndex(options.One("head-manifest")), sameRepository, actorIsBot);
        await File.AppendAllLinesAsync(options.One("github-output"),
        [
            $"required={Flag(result.Required)}",
            $"trusted={Flag(result.Trusted)}",
            $"violation={result.Violation}",
            $"index-switch={Flag(result.IndexSwitch)}",
            $"reason={result.Reason}",
        ]);
        Console.WriteLine(result.Reason);
        return ExitCodes.Passed;
    });

    private static string Flag(bool value) => value ? "true" : "false";

    // Only retrieval.index is read, so a head manifest that fails full validation isn't trusted any further.
    private static string LiveIndex(string path)
    {
        var yaml = new YamlStream();
        try
        {
            using var reader = File.OpenText(path);
            yaml.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }

        return yaml.Documents is [{ RootNode: YamlMappingNode root }]
            && root.Children.TryGetValue(new YamlScalarNode("retrieval"), out var retrieval) && retrieval is YamlMappingNode section
            && section.Children.TryGetValue(new YamlScalarNode("index"), out var index) && index is YamlScalarNode { Value: { Length: > 0 } name }
            ? name
            : throw new InvalidDataException($"{path} has no retrieval.index.");
    }
}
