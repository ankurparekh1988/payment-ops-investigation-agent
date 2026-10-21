using Azure.Identity;

using PaymentOps.Agent;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

namespace PaymentOps.Evaluation.Agent;

/// <summary>
/// agent-run: the seeded tasks against the deployed model and index, signed in as whoever ran
/// <c>az login</c>. Calls the model and queries Search; writes only the report.
/// </summary>
internal static class AgentRunCommand
{
    public const string Usage = "PaymentOps.Evaluation agent-run --foundry <url> --search <url> --scope <token scope> --ai <dir> --dataset <file> --out <file> --summary <file>";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["--foundry", var foundryUrl, "--search", var searchUrl, "--scope", var scope, "--ai", var ai, "--dataset", var datasetPath, "--out", var outPath, "--summary", var summaryPath]
            || !TryHttps(foundryUrl, out var foundry) || !TryHttps(searchUrl, out var search))
        {
            Console.Error.WriteLine($"Usage: {Usage}");
            return 2;
        }

        try
        {
            var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(ai));
            var tasks = AgentTaskDataset.Load(datasetPath);
            var report = await AgentRun.RunAsync(tasks, release, new InvestigationEndpoints(search, foundry, scope), new AzureCliCredential());

            await File.WriteAllTextAsync(outPath, report.ToJson());
            await File.AppendAllTextAsync(summaryPath, report.ToMarkdown());
            if (!report.GuardPassed)
            {
                GitHubAnnotation.Error("Agent live run", "No task produced a multi-tool trajectory ending in a validated assessment.");
                return 1;
            }

            return 0;
        }
        catch (AiReleaseException ex)
        {
            GitHubAnnotation.Errors("AI release", ex.Errors);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or AuthenticationFailedException)
        {
            GitHubAnnotation.Error("Agent live run", ex.Message);
            return 1;
        }
    }

    private static bool TryHttps(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;
}
