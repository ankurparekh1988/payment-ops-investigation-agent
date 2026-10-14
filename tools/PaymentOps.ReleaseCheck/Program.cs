// Checks the AI release in a working tree with the same loader the app uses, and optionally
// compares its prompt versions with a base branch's manifest.
//
// Usage: PaymentOps.ReleaseCheck <ai-directory> [--base <manifest-file>]

using PaymentOps.Platform.AiRelease;

if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--base"))
{
    Console.Error.WriteLine("Usage: PaymentOps.ReleaseCheck <ai-directory> [--base <manifest-file>]");
    return 2;
}

var aiDirectory = args[0];
var errors = new List<string>();

try
{
    var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(aiDirectory));
    Console.WriteLine($"AI release is valid: {release.Prompts.Count} prompts, chat deployment {release.ActiveChatDeployment}.");
}
catch (AiReleaseException ex)
{
    errors.AddRange(ex.Errors);
}

if (args.Length == 3)
{
    var head = File.ReadAllBytes(Path.Combine(aiDirectory, AiReleaseLoader.ManifestPath));
    errors.AddRange(PromptVersionCheck.Compare(File.ReadAllBytes(args[2]), head));
}

foreach (var error in errors)
{
    Console.Error.WriteLine($"::error file=ai/{AiReleaseLoader.ManifestPath}::{error}");
}

return errors.Count == 0 ? 0 : 1;
