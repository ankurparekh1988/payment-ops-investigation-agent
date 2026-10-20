namespace PaymentOps.Agent.Contracts;

/// <summary>
/// A cited section as the current user may see it now. Missing and not permitted are the same
/// <see cref="NotFound"/>, so a Restricted section's existence is never revealed.
/// </summary>
public abstract record SourceResult
{
    private SourceResult()
    {
    }

    public sealed record Found(string Title, string SectionPath, string Content) : SourceResult;

    public sealed record NotFound : SourceResult;

    public sealed record Unavailable : SourceResult;
}
