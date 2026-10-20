namespace PaymentOps.Agent.Answering;

/// <summary>Limits on one question. Defaults suit the Free tier; environments may override them.</summary>
public sealed class AnsweringOptions
{
    public const string SectionName = "PaymentOps:Answering";

    public int MaxQuestionLength { get; set; } = 1000;

    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan ModelTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public int MaxOutputTokens { get; set; } = 1500;
}
