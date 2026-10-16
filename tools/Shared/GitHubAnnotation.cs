namespace PaymentOps.Tools.Shared;

/// <summary>Writes errors as GitHub workflow annotations, which also read fine in a local terminal.</summary>
internal static class GitHubAnnotation
{
    public static void Error(string title, string message)
    {
        // Workflow commands end at a newline, so the message is escaped as GitHub requires.
        var escaped = message.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
        Console.Error.WriteLine($"::error title={title}::{escaped}");
    }

    public static void Errors(string title, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Error(title, message);
        }
    }
}
