namespace PaymentOps.Tools;

public enum ToolErrorCode
{
    InvalidArgument,
    RangeTooLarge,
    NotFound,
    Unavailable,
    Timeout,
    KnowledgeUnavailable,
    NotAuthorized,
    LimitReached,
}

/// <summary>What a tool call produced: a body for the model with the evidence behind it, or an error it can act on.</summary>
public abstract record ToolResult
{
    private ToolResult()
    {
    }

    /// <param name="Body">Serialized for the model as is; every record and computed fact in it names its evidence id.</param>
    public sealed record Ok(object Body, IReadOnlyList<EvidenceItem> Evidence, IReadOnlyList<Truncation> Truncations) : ToolResult;

    /// <param name="Message">Fixed wording the model can act on; never an exception's text.</param>
    /// <param name="Source">The data source concerned, when there is one.</param>
    public sealed record Failed(ToolErrorCode Code, string Message, string? Source = null) : ToolResult;
}

/// <summary>A list cut short to keep the result small; the turn records it as a gap.</summary>
public sealed record Truncation(string What, int Shown, int Total);
