namespace PaymentOps.Agent.Contracts;

/// <summary>What the UI may ask of the investigation side. Retrieval mode, prompts and models stay behind it.</summary>
public interface IInvestigationService
{
    /// <summary>Investigates with the read-only tools and returns an assessment the server has validated.</summary>
    Task<AssessmentResult> InvestigateAsync(string question, CancellationToken cancellationToken);

    Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken);
}
