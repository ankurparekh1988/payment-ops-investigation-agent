namespace PaymentOps.Agent.Contracts;

/// <summary>What the UI may ask of the investigation side. Retrieval mode, prompts and models stay behind it.</summary>
public interface IInvestigationService
{
    Task<InvestigationResult> AskAsync(string question, CancellationToken cancellationToken);

    Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken);
}
