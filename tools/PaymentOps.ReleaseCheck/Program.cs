// Checks the AI release in a working tree with the same loader the app uses and, given the base
// branch's manifest, the rules for how a release may change. Errors are written as GitHub annotations.
//
// Usage: PaymentOps.ReleaseCheck <ai-directory> [--base <manifest-file>]

using PaymentOps.Platform.Ai;

if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--base"))
{
    Console.Error.WriteLine("Usage: PaymentOps.ReleaseCheck <ai-directory> [--base <manifest-file>]");
    return 2;
}

var errors = new List<string>();

try
{
    var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(args[0]));
    Console.WriteLine($"AI release is valid: {release.Prompts.Count} prompts, chat deployment {release.ActiveChatDeployment}.");

    // Change rules only mean something once the release itself is valid.
    if (args.Length == 3)
    {
        errors.AddRange(File.Exists(args[2])
            ? ReleaseChangeCheck.Compare(File.ReadAllBytes(args[2]), release)
            : [$"Base manifest '{args[2]}' doesn't exist."]);
    }
}
catch (AiReleaseException ex)
{
    errors.AddRange(ex.Errors);
}

foreach (var error in errors)
{
    // Workflow commands end at a newline, so the message is escaped as GitHub requires.
    var escaped = error.Replace("%", "%25", StringComparison.Ordinal)
        .Replace("\r", "%0D", StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal);
    Console.Error.WriteLine($"::error title=AI release::{escaped}");
}

return errors.Count == 0 ? 0 : 1;
