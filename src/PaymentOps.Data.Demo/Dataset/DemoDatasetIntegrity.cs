using System.Text.RegularExpressions;

using PaymentOps.Domain.Settlement;

namespace PaymentOps.Data.Demo.Dataset;

/// <summary>
/// The rules that make the dataset one consistent system of record: references resolve, payouts trace to
/// the files they were paid from, times are UTC, and nothing has happened after the as-of time.
/// </summary>
internal static partial class DemoDatasetIntegrity
{
    public static IReadOnlyList<string> Check(DemoDataset dataset)
    {
        var errors = new List<string>();

        // JSON can hold a null array element that the serializer accepts; nothing else can be checked past one.
        CheckNoNulls(dataset, errors);
        if (errors.Count > 0)
        {
            return errors;
        }

        var asOf = dataset.AsOf;
        Utc("asOf", "the dataset", asOf, errors);

        var processors = Unique("processor", dataset.Processors.Select(p => p.Id), errors);
        var merchants = Unique("merchant", dataset.Merchants.Select(m => m.Id), errors);
        var payments = Unique("payment", dataset.Payments.Select(p => p.Id), errors);
        var files = Unique("settlement file", dataset.Files.Select(f => f.Id), errors);
        Unique("payout", dataset.Payouts.Select(p => p.Id), errors);
        Unique("incident", dataset.Incidents.Select(i => i.Id), errors);
        Unique("error event", dataset.ErrorEvents.Select(e => e.Id), errors);

        foreach (var processor in dataset.Processors)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(processor.Configuration.TimeZoneId, out _))
            {
                errors.Add($"processor {processor.Id}: unknown time zone '{processor.Configuration.TimeZoneId}'.");
            }
        }

        var closures = new HashSet<DateOnly>();
        foreach (var closure in dataset.ClosureDays)
        {
            if (closure.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                errors.Add($"closure day {closure.Date:yyyy-MM-dd} is a weekend, which is never a settlement day anyway.");
            }

            if (!closures.Add(closure.Date))
            {
                errors.Add($"closure day {closure.Date:yyyy-MM-dd} is listed more than once.");
            }
        }

        var merchantById = dataset.Merchants.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var merchant in dataset.Merchants)
        {
            foreach (var processorId in merchant.ProcessorIds.Where(id => !processors.Contains(id)))
            {
                errors.Add($"merchant {merchant.Id}: routed to unknown processor '{processorId}'.");
            }

            if (merchant.RiskHold is { } hold)
            {
                Utc("placedAt", $"merchant {merchant.Id} hold {hold.Id}", hold.PlacedAt, errors);
                NotAfter("placed", $"merchant {merchant.Id} hold {hold.Id}", hold.PlacedAt, asOf, errors);
            }
        }

        var listings = dataset.Files.SelectMany(f => f.Lines.SelectMany(l => l.PaymentIds.Select(id => (PaymentId: id, File: f))))
            .ToLookup(x => x.PaymentId, x => x.File);
        foreach (var payment in dataset.Payments)
        {
            var name = $"payment {payment.Id}";
            Money(name, payment.Amount, payment.Currency, errors);
            Utc("capturedAt", name, payment.CapturedAt, errors);
            NotAfter("captured", name, payment.CapturedAt, asOf, errors);
            if (payment.ProcessorCapturedAt is { } processorCapturedAt)
            {
                Utc("processorCapturedAt", name, processorCapturedAt, errors);
                NotAfter("captured by the processor", name, processorCapturedAt, asOf, errors);
            }

            if (!merchantById.TryGetValue(payment.MerchantId, out var merchant))
            {
                errors.Add($"{name}: unknown merchant '{payment.MerchantId}'.");
            }
            else if (!merchant.ProcessorIds.Contains(payment.ProcessorId))
            {
                errors.Add($"{name}: merchant {merchant.Id} isn't routed to processor '{payment.ProcessorId}'.");
            }

            foreach (var file in listings[payment.Id].Where(f => f.BatchDate != payment.BatchDate))
            {
                errors.Add($"{name}: batch date {payment.BatchDate:yyyy-MM-dd} doesn't match {file.Id}, which lists it for {file.BatchDate:yyyy-MM-dd}.");
            }

            if (payment.BatchDate is not null && !listings[payment.Id].Any())
            {
                errors.Add($"{name}: has a batch date but no file lists it.");
            }
        }

        var paymentById = dataset.Payments.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var file in dataset.Files)
        {
            var name = $"settlement file {file.Id}";
            Utc("receivedAt", name, file.ReceivedAt, errors);
            NotAfter("received", name, file.ReceivedAt, asOf, errors);
            Known("processor", file.ProcessorId, processors, name, errors);
            foreach (var line in file.Lines)
            {
                Money($"{name} line for {line.MerchantId}", line.Amount, line.Currency, errors);
                Known("merchant", line.MerchantId, merchants, name, errors);
                foreach (var paymentId in line.PaymentIds)
                {
                    if (!paymentById.TryGetValue(paymentId, out var payment))
                    {
                        errors.Add($"{name}: lists unknown payment '{paymentId}'.");
                    }
                    else if (payment.MerchantId != line.MerchantId || payment.ProcessorId != file.ProcessorId)
                    {
                        errors.Add($"{name}: lists {paymentId} under merchant {line.MerchantId}, but it's {payment.MerchantId}'s payment through {payment.ProcessorId}.");
                    }
                }
            }
        }

        var fileById = dataset.Files.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var payout in dataset.Payouts)
        {
            var name = $"payout {payout.Id}";
            Money(name, payout.Amount, payout.Currency, errors);
            Known("merchant", payout.MerchantId, merchants, name, errors);
            Known("processor", payout.ProcessorId, processors, name, errors);
            CheckPayoutTiming(payout, name, asOf, errors);

            if (payout.SourceFileIds.Count == 0)
            {
                errors.Add($"{name}: has no source file.");
            }

            foreach (var fileId in payout.SourceFileIds)
            {
                if (!fileById.TryGetValue(fileId, out var file))
                {
                    errors.Add($"{name}: source file '{fileId}' doesn't exist.");
                }
                else if (file.Status != SettlementFileStatus.Accepted)
                {
                    errors.Add($"{name}: source file {fileId} was {file.Status}, and a payout is only made from an accepted file.");
                }
                else if (file.ProcessorId != payout.ProcessorId || file.BatchDate != payout.BatchDate)
                {
                    errors.Add($"{name}: source file {fileId} is {file.ProcessorId}'s batch for {file.BatchDate:yyyy-MM-dd}, not {payout.ProcessorId}'s for {payout.BatchDate:yyyy-MM-dd}.");
                }
                else if (!file.Lines.Any(l => l.MerchantId == payout.MerchantId))
                {
                    errors.Add($"{name}: source file {fileId} has no line for merchant {payout.MerchantId}.");
                }
            }
        }

        foreach (var incident in dataset.Incidents)
        {
            var name = $"incident {incident.Id}";
            Known("processor", incident.ProcessorId, processors, name, errors);
            Utc("startedAt", name, incident.StartedAt, errors);
            NotAfter("started", name, incident.StartedAt, asOf, errors);
            if (incident.ResolvedAt is { } resolvedAt)
            {
                Utc("resolvedAt", name, resolvedAt, errors);
                NotAfter("resolved", name, resolvedAt, asOf, errors);
                if (resolvedAt <= incident.StartedAt)
                {
                    errors.Add($"{name}: resolved before it started.");
                }
            }
        }

        foreach (var errorEvent in dataset.ErrorEvents)
        {
            var name = $"error event {errorEvent.Id}";
            Known("processor", errorEvent.ProcessorId, processors, name, errors);
            Utc("occurredAt", name, errorEvent.OccurredAt, errors);
            NotAfter("occurred", name, errorEvent.OccurredAt, asOf, errors);
            if (errorEvent.MerchantId is { } merchantId)
            {
                Known("merchant", merchantId, merchants, name, errors);
            }

            if (errorEvent.FileId is { } fileId)
            {
                Known("settlement file", fileId, files, name, errors);
            }
        }

        return errors;
    }

    // A payout due after the as-of date can only still be waiting; one that was paid or held has happened.
    private static void CheckPayoutTiming(Payout payout, string name, DateTimeOffset asOf, List<string> errors)
    {
        if (payout.ScheduledFor > DateOnly.FromDateTime(asOf.UtcDateTime) && payout.Status != PayoutStatus.Scheduled)
        {
            errors.Add($"{name}: is {payout.Status} but due {payout.ScheduledFor:yyyy-MM-dd}, after the as-of date.");
        }

        if ((payout.Status == PayoutStatus.Paid) != (payout.PaidAt is not null))
        {
            errors.Add($"{name}: a paid time goes with status Paid, and only with it.");
        }

        if (payout.PaidAt is { } paidAt)
        {
            Utc("paidAt", name, paidAt, errors);
            NotAfter("paid", name, paidAt, asOf, errors);
        }
    }

    private static void CheckNoNulls(DemoDataset dataset, List<string> errors)
    {
        (string Section, IEnumerable<object?> Items)[] sections =
        [
            ("processors", dataset.Processors), ("closureDays", dataset.ClosureDays), ("merchants", dataset.Merchants),
            ("payments", dataset.Payments), ("files", dataset.Files), ("payouts", dataset.Payouts),
            ("incidents", dataset.Incidents), ("errorEvents", dataset.ErrorEvents),
            ("file lines", dataset.Files.Where(f => f is not null).SelectMany(f => f.Lines)),
        ];
        foreach (var (section, items) in sections)
        {
            if (items.Any(item => item is null))
            {
                errors.Add($"{section}: contains an empty (null) entry.");
            }
        }
    }

    private static HashSet<string> Unique(string kind, IEnumerable<string> ids, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"a {kind} has an empty id.");
            }
            else if (!seen.Add(id))
            {
                errors.Add($"{kind} {id}: appears more than once.");
            }
        }

        return seen;
    }

    private static void Known(string kind, string id, HashSet<string> ids, string owner, List<string> errors)
    {
        if (!ids.Contains(id))
        {
            errors.Add($"{owner}: unknown {kind} '{id}'.");
        }
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

    // A local offset would silently shift cut-off and calendar reasoning, so every time is written in UTC.
    private static void Utc(string field, string owner, DateTimeOffset value, List<string> errors)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            errors.Add($"{owner}: {field} must be UTC (Z), not offset {value.Offset}.");
        }
    }

    private static void NotAfter(string what, string owner, DateTimeOffset value, DateTimeOffset asOf, List<string> errors)
    {
        if (value > asOf)
        {
            errors.Add($"{owner}: {what} after the as-of time {asOf:u}.");
        }
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCode();
}
