namespace PaymentOps.Agent.Contracts;

/// <summary>What the UI may ask of the investigation side. Retrieval mode, prompts and models stay behind it.</summary>
public interface IInvestigationService
{
    /// <summary>
    /// Investigates with the read-only tools and returns an assessment the server has validated. Cancelling returns
    /// <see cref="NotAssessedReason.Cancelled"/>, and nothing from that investigation is kept.
    /// </summary>
    Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Where this conversation stands: what the next question would carry, and whether it can be asked.</summary>
    ConversationStatus Conversation { get; }

    /// <summary>Starts a new investigation: earlier questions are forgotten, nothing is carried over.</summary>
    void StartNew();

    Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken);
}
