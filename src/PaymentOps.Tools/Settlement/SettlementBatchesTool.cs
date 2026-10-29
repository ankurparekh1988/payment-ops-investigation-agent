using Microsoft.Extensions.Options;

using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Merchants;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;
using PaymentOps.Domain.Settlement;

namespace PaymentOps.Tools.Settlement;

public sealed record SettlementBatchesRequest(string? MerchantId, string? StartDate, string? EndDate);

public sealed record SettlementActivity(string MerchantId, string EvidenceId, IReadOnlyList<ProcessorBatches> Processors, IReadOnlyList<NonSettlementDay> NonSettlementDays);

public sealed record ProcessorBatches(string Id, string Name, string EvidenceId, ListOf<Batch> Batches);

/// <param name="MissingFile">Set when payments are expected in this batch, its file is due, and no file arrived. A rejected file isn't missing.</param>
/// <param name="PaymentSets">Set when the processor sent more than one accepted file for this batch date.</param>
public sealed record Batch(DateOnly Date, ListOf<PaymentView> Payments, IReadOnlyList<FileView> Files, IReadOnlyList<PayoutView> Payouts, FactRef? MissingFile, PaymentSetsView? PaymentSets);

/// <param name="DecidingCaptureTime">The processor's capture time where recorded, else ours; it decides the batch.</param>
/// <param name="LocalCaptureTime">That time in the processor's time zone.</param>
/// <param name="BatchDate">The processor's actual assignment; null until a file lists the payment.</param>
public sealed record PaymentView(
    string Id,
    decimal Amount,
    string Currency,
    DateTimeOffset CapturedAt,
    DateTimeOffset? ProcessorCapturedAt,
    DateTimeOffset DecidingCaptureTime,
    DateTime LocalCaptureTime,
    bool AfterCutOff,
    DatedFact ExpectedBatch,
    DateOnly? BatchDate,
    string EvidenceId);

/// <param name="MerchantLine">This merchant's share of the file; null when the file has no line for it.</param>
public sealed record FileView(string Id, DateTimeOffset ReceivedAt, string Status, MerchantLineView? MerchantLine, string EvidenceId);

public sealed record MerchantLineView(int PaymentCount, decimal Amount, string Currency);

/// <param name="Overdue">Set when the payout is still scheduled and was due before today.</param>
public sealed record PayoutView(string Id, string Status, DateOnly ScheduledFor, DateTimeOffset? PaidAt, IReadOnlyList<string> SourceFileIds, decimal Amount, string Currency, string EvidenceId, FactRef? Overdue);

public sealed record PaymentSetsView(PaymentSetRelation Relation, IReadOnlyList<string> FileIds, string EvidenceId);

public sealed record NonSettlementDay(DateOnly Date, string Reason, string EvidenceId);

public sealed record FactRef(string EvidenceId);

public sealed record DatedFact(DateOnly Date, string EvidenceId);

/// <summary>
/// What happened to a merchant's money over a range: per processor and batch date, the payments, the files,
/// the payouts and the facts computed from them. Facts only; the agent draws the conclusions.
/// </summary>
public sealed class SettlementBatchesTool(
    IMerchantData merchants,
    ISettlementData settlement,
    IProcessorData processors,
    IInvestigationClock clock,
    IOptions<ToolOptions> options) : InvestigationTool<SettlementBatchesRequest>(options)
{
    // Far enough past the range for a late capture's batch to land after a long run of closures.
    private const int CalendarLookaheadDays = 14;

    // A capture's day in the processor's zone, or by the processor's own clock, can be the day before the range starts.
    private const int CalendarLookbehindDays = 2;

    public override string Name => ToolNames.SettlementBatches;

    public override string Description =>
        "A merchant's settlement from start_date to end_date, both included: per processor and batch date, the payments (with capture time " +
        "against the cut-off and the batch they belong in), the settlement files received, the payouts, whether a due file is missing, " +
        "and how files for the same batch relate by their payments. Also lists non-settlement days in the range.";

    public override string Source => DataSources.Settlement;

    protected override async Task<ToolResult> RunCoreAsync(SettlementBatchesRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        var merchantId = ToolArguments.Id(request.MerchantId, "merchant_id");
        var range = ToolArguments.Range(request.StartDate, request.EndDate, clock.Today);
        if (await ReadAsync(token => merchants.FindAsync(merchantId, token), DataSources.Merchants, cancellationToken) is not { } merchant)
        {
            return NotFound("merchant", merchantId);
        }

        var calendar = (await ReadAsync(token => settlement.GetCalendarAsync(range.Start.AddDays(-CalendarLookbehindDays), range.EndExclusive.AddDays(CalendarLookaheadDays), token), DataSources.Settlement, cancellationToken))
            .ToDictionary(day => day.Date);
        var payments = await ReadAsync(token => settlement.GetPaymentsAsync(merchant.Id, range.FromUtc, range.ToUtcExclusive, token), DataSources.Settlement, cancellationToken);

        var routed = new List<Processor>();
        foreach (var processorId in merchant.ProcessorIds)
        {
            if (await ReadAsync(token => processors.FindAsync(processorId, token), DataSources.Processors, cancellationToken) is { } processor)
            {
                routed.Add(processor);
            }
        }

        var call = new Call(clock, merchant, calendar, Collect());
        var timings = payments.ToDictionary(
            p => p.Id,
            p => routed.Find(r => r.Id == p.ProcessorId) is { } processor ? SettlementTiming.Timing(p, processor.Configuration, call.IsSettlementDay) : null);
        var batchOf = payments.Where(p => timings[p.Id] is not null).ToDictionary(p => p.Id, p => p.BatchDate ?? timings[p.Id]!.ExpectedBatchDate);

        // A capture's batch can fall outside the range, so files and payouts are read across every batch seen.
        var readFrom = batchOf.Values.Append(range.Start).Min();
        var readTo = batchOf.Values.Append(range.End).Max().AddDays(1);
        var payouts = (await ReadAsync(token => settlement.GetPayoutsAsync(merchant.Id, readFrom, readTo, token), DataSources.Settlement, cancellationToken))
            .ToLookup(p => p.ProcessorId);

        var views = new List<ProcessorBatches>();
        foreach (var processor in routed)
        {
            var files = await ReadAsync(token => settlement.GetFilesAsync(processor.Id, readFrom, readTo, token), DataSources.Settlement, cancellationToken);
            var own = payments.Where(p => p.ProcessorId == processor.Id && batchOf.ContainsKey(p.Id)).ToList();
            views.Add(call.Batches(processor, own, timings, batchOf, files, [.. payouts[processor.Id]]));
        }

        var nonSettlementDays = calendar.Values
            .Where(day => !day.IsSettlementDay && day.Date >= range.Start && day.Date <= range.End)
            .OrderBy(day => day.Date)
            .Select(day => new NonSettlementDay(day.Date, Reason(day), call.NonSettlementDay(day.Date)))
            .ToList();

        return call.Evidence.Ok(new SettlementActivity(merchant.Id, call.Evidence.Record(EvidenceIds.Merchant(merchant.Id), merchant.Name), views, nonSettlementDays));
    }

    // The port lists weekends with no reason; closure days carry theirs.
    // A closure reason is written outside this system; "Weekend" is ours.
    private static string Reason(SettlementDay day) => day.ClosureReason is { } reason ? UntrustedText.Wrap(reason) : "Weekend";

    /// <summary>One call's view building, sharing the calendar, the clock and the evidence being collected.</summary>
    private sealed class Call(IInvestigationClock clock, Merchant merchant, Dictionary<DateOnly, SettlementDay> calendar, EvidenceCollector evidence)
    {
        public EvidenceCollector Evidence => evidence;

        // The calendar was read well past the range; a day outside it is a bug, not a settlement day.
        public bool IsSettlementDay(DateOnly date) =>
            calendar.TryGetValue(date, out var day) ? day.IsSettlementDay : throw new InvalidOperationException($"The calendar wasn't read as far as {date:yyyy-MM-dd}.");

        public ProcessorBatches Batches(
            Processor processor,
            List<Payment> payments,
            Dictionary<string, PaymentTiming?> timings,
            Dictionary<string, DateOnly> batchOf,
            IReadOnlyList<SettlementFile> files,
            List<Payout> payouts)
        {
            var paymentsByDate = payments.ToLookup(p => batchOf[p.Id]);
            var filesByDate = files.ToLookup(f => f.BatchDate);
            var payoutsByDate = payouts.ToLookup(p => p.BatchDate);
            var dates = paymentsByDate.Select(g => g.Key)
                .Concat(payoutsByDate.Select(g => g.Key))
                .Concat(files.Where(f => f.Lines.Any(l => l.MerchantId == merchant.Id)).Select(f => f.BatchDate))
                .Distinct()
                .Order();

            var batches = dates.Select(date =>
            {
                var batchPayments = paymentsByDate[date].ToList();
                var batchFiles = filesByDate[date].ToList();
                return new Batch(
                    date,
                    evidence.Cap(batchPayments.ConvertAll(p => Payment(p, processor, timings[p.Id]!)), $"{processor.Id} payments for {date:yyyy-MM-dd}"),
                    batchFiles.ConvertAll(File),
                    [.. payoutsByDate[date].Select(Payout)],
                    MissingFile(processor, date, batchPayments, batchFiles),
                    PaymentSets(processor, date, batchFiles));
            }).ToList();

            // Truncation notes become gaps, which later turns carry to the model, so they name the processor by id.
            return new ProcessorBatches(processor.Id, UntrustedText.Wrap(processor.Name), evidence.Record(EvidenceIds.Processor(processor.Id), processor.Name), evidence.Cap(batches, $"{processor.Id} batches"));
        }

        public string NonSettlementDay(DateOnly date)
        {
            var reason = Reason(calendar[date]);
            var calendarId = evidence.Record(EvidenceIds.CalendarDay(date), $"{date:yyyy-MM-dd}: {reason}");
            return evidence.Derive(EvidenceIds.NonSettlementDay(date), $"{date:yyyy-MM-dd} isn't a settlement day ({reason})", [calendarId]);
        }

        // Missing is only ever claimed against payments expected in the batch, so an empty source can't look like an outage.
        // "A file we received but rejected is not missing" (missing-settlement-batch): any file for the batch counts as arrived.
        private FactRef? MissingFile(Processor processor, DateOnly date, List<Payment> batchPayments, List<SettlementFile> batchFiles)
        {
            if (batchPayments.Count == 0 || batchFiles.Count > 0 || !SettlementTiming.FileDue(date, processor.Configuration, clock.Now))
            {
                return null;
            }

            return new FactRef(evidence.Derive(
                EvidenceIds.MissingFile(processor.Id, date),
                $"No {processor.Name} file arrived for {date:yyyy-MM-dd}",
                [.. batchPayments.Select(p => EvidenceIds.Payment(p.Id)), EvidenceIds.Processor(processor.Id)]));
        }

        private PaymentSetsView? PaymentSets(Processor processor, DateOnly date, List<SettlementFile> batchFiles)
        {
            var accepted = batchFiles.Where(f => f.Status == SettlementFileStatus.Accepted).OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
            if (accepted.Count < 2)
            {
                return null;
            }

            var relation = SettlementTiming.Relate([.. accepted.Select(f => f.Lines.SelectMany(l => l.PaymentIds).ToList())]);
            var fileIds = accepted.ConvertAll(f => f.Id);
            var id = evidence.Derive(
                EvidenceIds.PaymentSets(processor.Id, date),
                $"{processor.Name} files for {date:yyyy-MM-dd} list {ToolJson.Word(relation)} payments",
                [.. fileIds.Select(EvidenceIds.File)]);
            return new PaymentSetsView(relation, fileIds, id);
        }

        private PaymentView Payment(Payment payment, Processor processor, PaymentTiming timing)
        {
            var paymentId = evidence.Record(EvidenceIds.Payment(payment.Id), $"Payment {payment.Id}");
            foreach (var skipped in timing.SkippedDays)
            {
                NonSettlementDay(skipped);
            }

            var expected = evidence.Derive(
                EvidenceIds.ExpectedBatch(payment.Id),
                $"{payment.Id} belongs in the {timing.ExpectedBatchDate:yyyy-MM-dd} batch",
                [paymentId, EvidenceIds.Processor(processor.Id), .. timing.SkippedDays.Select(EvidenceIds.CalendarDay)]);
            return new PaymentView(
                payment.Id, payment.Amount, payment.Currency, payment.CapturedAt, payment.ProcessorCapturedAt, timing.DecidingCaptureTime,
                timing.LocalCaptureTime, timing.AfterCutOff, new DatedFact(timing.ExpectedBatchDate, expected), payment.BatchDate, paymentId);
        }

        private FileView File(SettlementFile file)
        {
            var lines = file.Lines.Where(l => l.MerchantId == merchant.Id).ToList();
            var line = lines.Count == 0 ? null : new MerchantLineView(lines.Sum(l => l.PaymentIds.Count), lines.Sum(l => l.Amount), lines[0].Currency);
            return new FileView(file.Id, file.ReceivedAt, ToolJson.Word(file.Status), line, evidence.Record(EvidenceIds.File(file.Id), $"Settlement file {file.Id}"));
        }

        private PayoutView Payout(Payout payout)
        {
            var payoutId = evidence.Record(EvidenceIds.Payout(payout.Id), $"Payout {payout.Id}");
            var overdue = payout.Status == PayoutStatus.Scheduled && payout.ScheduledFor < clock.Today
                ? new FactRef(evidence.Derive(EvidenceIds.Overdue(payout.Id), $"{payout.Id} was due {payout.ScheduledFor:yyyy-MM-dd} and is still scheduled", [payoutId]))
                : null;
            return new PayoutView(payout.Id, ToolJson.Word(payout.Status), payout.ScheduledFor, payout.PaidAt, payout.SourceFileIds, payout.Amount, payout.Currency, payoutId, overdue);
        }
    }
}
