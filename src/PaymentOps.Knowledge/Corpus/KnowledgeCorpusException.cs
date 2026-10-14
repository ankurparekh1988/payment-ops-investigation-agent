namespace PaymentOps.Knowledge.Corpus;

/// <summary>The knowledge corpus is invalid. Carries every problem found, not just the first.</summary>
public sealed class KnowledgeCorpusException : Exception
{
    public KnowledgeCorpusException(IReadOnlyList<string> errors)
        : base("The knowledge corpus is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
