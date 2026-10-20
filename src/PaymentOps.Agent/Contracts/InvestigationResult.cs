namespace PaymentOps.Agent.Contracts;

/// <summary>An answer with validated citations, or the reason there isn't one.</summary>
public abstract record InvestigationResult
{
    private InvestigationResult()
    {
    }

    /// <summary>A grounded answer. Every citation was issued by the server for this question.</summary>
    public sealed record Answered(string Markdown, IReadOnlyList<Citation> Citations) : InvestigationResult;

    public sealed record NotAnswered(NotAnsweredReason Reason) : InvestigationResult;
}

/// <summary>A cited section, described from the server's own evidence, never from model output.</summary>
/// <param name="Id">The marker used in the answer, such as "S1".</param>
public sealed record Citation(string Id, string SectionKey, string Title, string SectionPath);
