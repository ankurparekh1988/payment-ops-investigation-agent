namespace PaymentOps.Platform.Ai;

/// <summary>The AI release is invalid. Carries every problem found, not just the first.</summary>
public sealed class AiReleaseException : Exception
{
    public AiReleaseException(IReadOnlyList<string> errors)
        : base("The AI release is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
