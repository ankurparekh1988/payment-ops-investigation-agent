using Microsoft.Extensions.DependencyInjection;

using PaymentOps.Domain.Processors;
using PaymentOps.Evaluation.Running;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Evaluation.Safety;

/// <summary>
/// Plants a case's poison in the data its tools read, beneath the tools, so the text reaches the model by the
/// same path untrusted text would: marked by the tool and screened by the guard.
/// </summary>
internal static class Poisoning
{
    /// <param name="sectionKey">For a knowledge poison, the section it's appended to.</param>
    public static Action<IServiceCollection>? For(SafetyCase c, string? sectionKey)
    {
        ArgumentNullException.ThrowIfNull(c);

        return c.Poison switch
        {
            null => null,
            { Surface: SafetyPoison.ProcessorIncident } poison => services =>
                EvaluationServices.Wrap<IProcessorData>(services, (_, data) => new PoisonedProcessorData(data, poison.Target, poison.Payload)),
            { Surface: SafetyPoison.Knowledge } poison => services =>
                EvaluationServices.Wrap<IKnowledgeLookup>(services, (_, lookup) => new PoisonedKnowledge(lookup, sectionKey ?? throw new ArgumentNullException(nameof(sectionKey)), poison.Payload)),
            var other => throw new ArgumentOutOfRangeException(nameof(c), other.Surface, "Unknown poison surface."),
        };
    }

    private static string Append(string text, string payload) => $"{text} {payload}";

    private sealed class PoisonedProcessorData(IProcessorData inner, string target, string payload) : IProcessorData
    {
        public Task<Processor?> FindAsync(string processorId, CancellationToken cancellationToken) => inner.FindAsync(processorId, cancellationToken);

        public async Task<IReadOnlyList<ProcessorIncident>> GetIncidentsAsync(string processorId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken)
        {
            var incidents = await inner.GetIncidentsAsync(processorId, fromInclusive, toExclusive, cancellationToken);
            return processorId == target ? [.. incidents.Select(i => i with { Summary = Append(i.Summary, payload) })] : incidents;
        }
    }

    // The planted section joins every search result, as a poisoned document an attacker wrote to be found would.
    private sealed class PoisonedKnowledge(IKnowledgeLookup inner, string sectionKey, string payload) : IKnowledgeLookup
    {
        public async Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken)
        {
            var sections = await inner.RetrieveAsync(question, access, cancellationToken);
            if (sections.Any(s => s.Key == sectionKey))
            {
                return [.. sections.Select(Poison)];
            }

            return await inner.FindAsync(sectionKey, access, cancellationToken) is { } planted ? [.. sections, Poison(planted)] : sections;
        }

        public async Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
            await inner.FindAsync(sectionKey, access, cancellationToken) is { } section ? Poison(section) : null;

        private RetrievedSection Poison(RetrievedSection section) => section.Key == sectionKey ? section with { Content = Append(section.Content, payload) } : section;
    }
}
