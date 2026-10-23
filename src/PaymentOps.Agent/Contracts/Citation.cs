namespace PaymentOps.Agent.Contracts;

/// <summary>A cited section, described from the server's own evidence, never from model output.</summary>
/// <param name="Id">The id the section had in its investigation, such as "S1".</param>
public sealed record Citation(string Id, string SectionKey, string Title, string SectionPath);
