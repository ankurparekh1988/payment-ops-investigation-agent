using System.Text.RegularExpressions;

using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>
/// The rules that make the dataset one consistent system of record: references resolve, payouts trace to
/// the files they were paid from, and nothing has happened after the as-of time. JSON shape, UTC times and
/// null list entries are already enforced by the loader.
/// </summary>
internal static partial class DemoDatasetIntegrity
{
    public static IReadOnlyList<string> Check(DemoDataset dataset)
    {
        var errors = new List<string>();
        var context = new Context(
            dataset.AsOf,
            Index("processor", dataset.Processors, p => p.Id, errors),
            Index("merchant", dataset.Merchants, m => m.Id, errors),
            Index("payment", dataset.Payments, p => p.Id, errors),
            Index("settlement file", dataset.Files, f => f.Id, errors),
            [.. dataset.ClosureDays.Select(c => c.Date)],
            errors);
        Index("payout", dataset.Payouts, p => p.Id, errors);
        Index("incident", dataset.Incidents, i => i.Id, errors);
        Index("error event", dataset.ErrorEvents, e => e.Id, errors);

        CheckProcessors(dataset, context);
        CheckClosureDays(dataset, context);
        CheckMerchants(dataset, context);
        CheckPayments(dataset, context);
        CheckFiles(dataset, context);
        CheckPayouts(dataset, context);
        CheckIncidentsAndEvents(dataset, context);
        return errors;
    }

    private static void CheckProcessors(DemoDataset dataset, Context context)
    {
        foreach (var processor in dataset.Processors)
        {
            var name = $"processor {processor.Id}";
            var configuration = processor.Configuration;

            // IANA ids only, as the record documents; Windows also resolves its own names.
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(configuration.TimeZoneId, out var zone) || !zone.HasIanaId)
            {
                context.Errors.Add($"{name}: '{configuration.TimeZoneId}' isn't an IANA time zone.");
            }

            if (configuration.HoldingSettlementDays < 0)
            {
                context.Errors.Add($"{name}: holding period can't be negative ({configuration.HoldingSettlementDays}).");
            }

            if (configuration.LateAfter < TimeSpan.Zero)
            {
                context.Errors.Add($"{name}: late-after allowance can't be negative ({configuration.LateAfter}).");
            }
        }
    }

    private static void CheckClosureDays(DemoDataset dataset, Context context)
    {
        var seen = new HashSet<DateOnly>();
        foreach (var closure in dataset.ClosureDays)
        {
            if (SettlementCalendar.IsWeekend(closure.Date))
            {
                context.Errors.Add($"closure day {closure.Date:yyyy-MM-dd} is a weekend, which is never a settlement day anyway.");
            }

            if (!seen.Add(closure.Date))
            {
                context.Errors.Add($"closure day {closure.Date:yyyy-MM-dd} is listed more than once.");
            }
        }
    }

    private static void CheckMerchants(DemoDataset dataset, Context context)
    {
        foreach (var merchant in dataset.Merchants)
        {
            foreach (var processorId in merchant.ProcessorIds)
            {
                context.Known("processor", processorId, context.Processors, $"merchant {merchant.Id}");
            }

            if (merchant.RiskHold is { } hold)
            {
                context.Happened("placed", $"merchant {merchant.Id} hold {hold.Id}", hold.PlacedAt);
            }
        }
    }

    private static void CheckPayments(DemoDataset dataset, Context context)
    {
        var listings = dataset.Files.SelectMany(f => f.Lines.SelectMany(l => l.PaymentIds.Select(id => (PaymentId: id, File: f))))
            .ToLookup(x => x.PaymentId, x => x.File);
        foreach (var payment in dataset.Payments)
        {
            var name = $"payment {payment.Id}";
            Money(name, payment.Amount, payment.Currency, context.Errors);
            context.Happened("captured", name, payment.CapturedAt);
            if (payment.ProcessorCapturedAt is { } processorCapturedAt)
            {
                context.Happened("captured by the processor", name, processorCapturedAt);
            }

            if (!context.Merchants.TryGetValue(payment.MerchantId, out var merchant))
            {
                context.Errors.Add($"{name}: unknown merchant '{payment.MerchantId}'.");
            }
            else if (!merchant.ProcessorIds.Contains(payment.ProcessorId))
            {
                context.Errors.Add($"{name}: merchant {merchant.Id} isn't routed to processor '{payment.ProcessorId}'.");
            }

            var files = listings[payment.Id].ToList();
            foreach (var file in files.Where(f => f.BatchDate != payment.BatchDate))
            {
                context.Errors.Add($"{name}: batch date {payment.BatchDate:yyyy-MM-dd} doesn't match {file.Id}, which lists it for {file.BatchDate:yyyy-MM-dd}.");
            }

            if (payment.BatchDate is not null && files.Count == 0)
            {
                context.Errors.Add($"{name}: has a batch date but no file lists it.");
            }
        }
    }

    private static void CheckFiles(DemoDataset dataset, Context context)
    {
        foreach (var file in dataset.Files)
        {
            var name = $"settlement file {file.Id}";
            context.Happened("received", name, file.ReceivedAt);
            context.Known("processor", file.ProcessorId, context.Processors, name);
            foreach (var line in file.Lines)
            {
                Money($"{name} line for {line.MerchantId}", line.Amount, line.Currency, context.Errors);
                context.Known("merchant", line.MerchantId, context.Merchants, name);
                var listed = line.PaymentIds.Where(context.Payments.ContainsKey).Select(id => context.Payments[id]).ToList();
                if (listed.Count == line.PaymentIds.Count
                    && (listed.Sum(p => p.Amount) != line.Amount || listed.Any(p => p.Currency != line.Currency)))
                {
                    context.Errors.Add($"{name}: the line for {line.MerchantId} is {line.Amount} {line.Currency}, but its payments add up to {listed.Sum(p => p.Amount)} {string.Join("/", listed.Select(p => p.Currency).Distinct())}.");
                }

                foreach (var paymentId in line.PaymentIds)
                {
                    if (!context.Payments.TryGetValue(paymentId, out var payment))
                    {
                        context.Errors.Add($"{name}: lists unknown payment '{paymentId}'.");
                    }
                    else if (payment.MerchantId != line.MerchantId || payment.ProcessorId != file.ProcessorId)
                    {
                        context.Errors.Add($"{name}: lists {paymentId} under merchant {line.MerchantId}, but it's {payment.MerchantId}'s payment through {payment.ProcessorId}.");
                    }
                }
            }
        }
    }

    private static void CheckPayouts(DemoDataset dataset, Context context)
    {
        foreach (var payout in dataset.Payouts)
        {
            var name = $"payout {payout.Id}";
            Money(name, payout.Amount, payout.Currency, context.Errors);
            context.Known("merchant", payout.MerchantId, context.Merchants, name);
            context.Known("processor", payout.ProcessorId, context.Processors, name);

            // A payout due after the as-of date can only still be waiting; one that was paid or held has happened.
            if (payout.ScheduledFor > DateOnly.FromDateTime(context.AsOf.UtcDateTime) && payout.Status != PayoutStatus.Scheduled)
            {
                context.Errors.Add($"{name}: is {payout.Status} but due {payout.ScheduledFor:yyyy-MM-dd}, after the as-of date.");
            }

            if ((payout.Status == PayoutStatus.Paid) != (payout.PaidAt is not null))
            {
                context.Errors.Add($"{name}: a paid time goes with status Paid, and only with it.");
            }

            if (payout.ScheduledFor <= payout.BatchDate || SettlementCalendar.IsWeekend(payout.ScheduledFor) || context.ClosureDays.Contains(payout.ScheduledFor))
            {
                context.Errors.Add($"{name}: due {payout.ScheduledFor:yyyy-MM-dd}, which isn't a settlement day after its batch date {payout.BatchDate:yyyy-MM-dd}.");
            }

            if (payout.SourceFileIds.Count == 0)
            {
                context.Errors.Add($"{name}: has no source file.");
            }

            var problems = payout.SourceFileIds.Select(id => SourceFileProblem(payout, id, context)).OfType<string>().ToList();
            context.Errors.AddRange(problems.Select(problem => $"{name}: {problem}"));
            if (problems.Count > 0 || payout.SourceFileIds.Count == 0)
            {
                continue;
            }

            var files = payout.SourceFileIds.Select(id => context.Files[id]).ToList();
            var lines = files.SelectMany(f => f.Lines).Where(l => l.MerchantId == payout.MerchantId).ToList();
            if (lines.Sum(l => l.Amount) != payout.Amount || lines.Any(l => l.Currency != payout.Currency))
            {
                context.Errors.Add($"{name}: is {payout.Amount} {payout.Currency}, but its source lines add up to {lines.Sum(l => l.Amount)} {string.Join("/", lines.Select(l => l.Currency).Distinct())}.");
            }

            if (payout.PaidAt is { } paidAt)
            {
                context.Happened("paid", name, paidAt);

                // Paid on or after the day it was due, and only once every source file had arrived.
                if (DateOnly.FromDateTime(paidAt.UtcDateTime) < payout.ScheduledFor || files.Any(f => paidAt < f.ReceivedAt))
                {
                    context.Errors.Add($"{name}: paid {paidAt:u}, before it was due or before its source files arrived.");
                }
            }
        }
    }

    private static string? SourceFileProblem(Payout payout, string fileId, Context context) =>
        !context.Files.TryGetValue(fileId, out var file) ? $"source file '{fileId}' doesn't exist."
        : file.Status != SettlementFileStatus.Accepted ? $"source file {fileId} was {file.Status}, and a payout is only made from an accepted file."
        : file.ProcessorId != payout.ProcessorId || file.BatchDate != payout.BatchDate
            ? $"source file {fileId} is {file.ProcessorId}'s batch for {file.BatchDate:yyyy-MM-dd}, not {payout.ProcessorId}'s for {payout.BatchDate:yyyy-MM-dd}."
        : !file.Lines.Any(l => l.MerchantId == payout.MerchantId) ? $"source file {fileId} has no line for merchant {payout.MerchantId}."
        : null;

    private static void CheckIncidentsAndEvents(DemoDataset dataset, Context context)
    {
        foreach (var incident in dataset.Incidents)
        {
            var name = $"incident {incident.Id}";
            context.Known("processor", incident.ProcessorId, context.Processors, name);
            context.Happened("started", name, incident.StartedAt);
            if (incident.ResolvedAt is { } resolvedAt)
            {
                context.Happened("resolved", name, resolvedAt);
                if (resolvedAt <= incident.StartedAt)
                {
                    context.Errors.Add($"{name}: resolved before it started.");
                }
            }
        }

        foreach (var errorEvent in dataset.ErrorEvents)
        {
            var name = $"error event {errorEvent.Id}";
            context.Known("processor", errorEvent.ProcessorId, context.Processors, name);
            context.Happened("occurred", name, errorEvent.OccurredAt);
            if (errorEvent.MerchantId is { } merchantId)
            {
                if (!context.Merchants.TryGetValue(merchantId, out var merchant))
                {
                    context.Errors.Add($"{name}: unknown merchant '{merchantId}'.");
                }
                else if (!merchant.ProcessorIds.Contains(errorEvent.ProcessorId))
                {
                    context.Errors.Add($"{name}: merchant {merchantId} isn't routed to {errorEvent.ProcessorId}.");
                }
            }

            if (errorEvent.FileId is { } fileId)
            {
                if (!context.Files.TryGetValue(fileId, out var file))
                {
                    context.Errors.Add($"{name}: unknown settlement file '{fileId}'.");
                }
                else if (file.ProcessorId != errorEvent.ProcessorId)
                {
                    context.Errors.Add($"{name}: file {fileId} is {file.ProcessorId}'s, not {errorEvent.ProcessorId}'s.");
                }
            }
        }
    }

    // Each id once; the first record with an id is the one references resolve to.
    private static Dictionary<string, T> Index<T>(string kind, IEnumerable<T> items, Func<T, string> id, List<string> errors)
    {
        var index = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = id(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add($"a {kind} has an empty id.");
            }
            else if (!index.TryAdd(key, item))
            {
                errors.Add($"{kind} {key}: appears more than once.");
            }
        }

        return index;
    }

    private static void Money(string owner, decimal amount, string currency, List<string> errors)
    {
        if (amount <= 0)
        {
            errors.Add($"{owner}: amount must be positive, not {amount}.");
        }

        if (!CurrencyCode().IsMatch(currency))
        {
            errors.Add($"{owner}: currency '{currency}' isn't a three-letter ISO 4217 code.");
        }
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCode();

    private sealed record Context(
        DateTimeOffset AsOf,
        Dictionary<string, Processor> Processors,
        Dictionary<string, Merchant> Merchants,
        Dictionary<string, Payment> Payments,
        Dictionary<string, SettlementFile> Files,
        HashSet<DateOnly> ClosureDays,
        List<string> Errors)
    {
        public void Known<T>(string kind, string id, Dictionary<string, T> index, string owner)
        {
            if (!index.ContainsKey(id))
            {
                Errors.Add($"{owner}: unknown {kind} '{id}'.");
            }
        }

        public void Happened(string what, string owner, DateTimeOffset value)
        {
            if (value > AsOf)
            {
                Errors.Add($"{owner}: {what} after the as-of time {AsOf:u}.");
            }
        }
    }
}
