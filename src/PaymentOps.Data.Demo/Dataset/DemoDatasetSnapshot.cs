using System.Collections.Immutable;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>
/// Copies every list in the dataset, nested ones included, into an immutable array. The adapters hand out the
/// dataset's own records, and <c>IReadOnlyList</c> alone doesn't stop a caller casting back to a mutable list.
/// </summary>
internal static class DemoDatasetSnapshot
{
    public static DemoDataset Freeze(DemoDataset dataset) => dataset with
    {
        Processors = Frozen(dataset.Processors),
        ClosureDays = Frozen(dataset.ClosureDays),
        Merchants = Frozen(dataset.Merchants.Select(m => m with { ProcessorIds = Frozen(m.ProcessorIds) })),
        Payments = Frozen(dataset.Payments),
        Files = Frozen(dataset.Files.Select(f => f with
        {
            Lines = Frozen(f.Lines.Select(l => l with { PaymentIds = Frozen(l.PaymentIds) })),
        })),
        Payouts = Frozen(dataset.Payouts.Select(p => p with { SourceFileIds = Frozen(p.SourceFileIds) })),
        Incidents = Frozen(dataset.Incidents),
        ErrorEvents = Frozen(dataset.ErrorEvents),
    };

    private static ImmutableArray<T> Frozen<T>(IEnumerable<T> items) => [.. items];
}
