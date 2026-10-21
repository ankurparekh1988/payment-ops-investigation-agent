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

/// <param name="MissingFile">Set when payments are expected in this batch, its file is due, and no accepted file arrived.</param>
/// <param name="PaymentSets">Set when the processor sent more than one file for this batch date.</param>
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
    IOptions<ToolOptions> options) : InvestigationTool<SettlementBatchesRequest>
{
    // Far enough past the range for a late capture's batch to land after a long weekend of closures.
    private const int CalendarLookaheadDays = 14;

    public override string Name => ToolNames.SettlementBatches;

    public override string Description =>
        "A merchant's settlement from start_date to end_date, both included: per processor and batch date, the payments (with capture time " +
        "against the cut-off and the batch they belong in), the settlement files received, the payouts, whether a due file is missing, " +
        "and how files for the same batch relate by their payments. Also lists non-settlement days in the range.";

    public override string Source => DataSources.Settlement;

    protected override async Task<ToolResult> RunCoreAsync(SettlementBatchesRequest request, IUserContext user, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryId(request.MerchantId, "merchant_id", out var merchantId, out var error))
        {
            return Invalid(error!);
        }

        if (!ToolArguments.TryRange(request.StartDate, request.EndDate, clock.Today, out var range, out var code, out error))
        {
            return Invalid(error!, code);
        }

        var backoff = options.Value.RetryBackoff;
        var merchant = await DataSourceCall.RunAsync(token => merchants.FindAsync(merchantId, token), DataSources.Merchants, backoff, cancellationToken);
        if (merchant is null)
        {
            return new ToolResult.Failed(ToolErrorCode.NotFound, $"No merchant has the id {merchantId}.");
        }

        var calendar = (await DataSourceCall.RunAsync(token => settlement.GetCalendarAsync(range.Start, range.EndExclusive.AddDays(CalendarLookaheadDays), token), DataSources.Settlement, backoff, cancellationToken))
            .ToDictionary(day => day.Date);
        var payments = await DataSourceCall.RunAsync(token => settlement.GetPaymentsAsync(merchant.Id, range.FromUtc, range.ToUtcExclusive, token), DataSources.Settlement, backoff, cancellationToken);

        var facts = new Facts(calendar, clock.Today);
        var processorViews = new List<ProcessorBatches>();
        foreach (var processorId in merchant.ProcessorIds)
        {
            var processor = await DataSourceCall.RunAsync(token => processors.FindAsync(processorId, token), DataSources.Processors, backoff, cancellationToken);
            if (processor is null)
            {
                continue;
            }

            facts.Records.Add(new RecordEvidence(EvidenceIds.Processor(processor.Id), processor.Name));
            processorViews.Add(await BatchesForAsync(merchant, processor, payments.Where(p => p.ProcessorId == processor.Id).ToList(), range, facts, cancellationToken));
        }

        var nonSettlementDays = calendar.Values
            .Where(day => !day.IsSettlementDay && day.Date >= range.Start && day.Date <= range.End)
            .OrderBy(day => day.Date)
            .Select(day => new NonSettlementDay(day.Date, Reason(day), facts.NonSettlementDay(day.Date)))
            .ToList();

        var activity = new SettlementActivity(merchant.Id, EvidenceIds.Merchant(merchant.Id), processorViews, nonSettlementDays);
        return new ToolResult.Ok(activity, [new RecordEvidence(EvidenceIds.Merchant(merchant.Id), merchant.Name), .. facts.Records, .. facts.Derived.Values], facts.Truncations);
    }

    private async Task<ProcessorBatches> BatchesForAsync(Merchant merchant, Processor processor, List<Payment> payments, DateRange range, Facts facts, CancellationToken cancellationToken)
    {
        var configuration = processor.Configuration;
        var timings = payments.ToDictionary(p => p.Id, p => SettlementTiming.Timing(p, configuration, facts.IsSettlementDay));
        var batchOf = payments.ToDictionary(p => p.Id, p => p.BatchDate ?? timings[p.Id].ExpectedBatchDate);

        // A late capture's batch can fall after the range, so files and payouts are read up to the last batch seen.
        var lastBatch = batchOf.Values.Append(range.End).Max();
        var backoff = options.Value.RetryBackoff;
        var files = await DataSourceCall.RunAsync(token => settlement.GetFilesAsync(processor.Id, range.Start, lastBatch.AddDays(1), token), DataSources.Settlement, backoff, cancellationToken);
        var payouts = (await DataSourceCall.RunAsync(token => settlement.GetPayoutsAsync(merchant.Id, range.Start, lastBatch.AddDays(1), token), DataSources.Settlement, backoff, cancellationToken))
            .Where(p => p.ProcessorId == processor.Id).ToList();

        var dates = batchOf.Values
            .Concat(payouts.Select(p => p.BatchDate))
            .Concat(files.Where(f => f.Lines.Any(l => l.MerchantId == merchant.Id)).Select(f => f.BatchDate))
            .Distinct()
            .Order()
            .ToList();

        var cap = options.Value.MaxListItems;
        var batches = new List<Batch>();
        foreach (var date in dates)
        {
            var batchPayments = payments.Where(p => batchOf[p.Id] == date).ToList();
            var batchFiles = files.Where(f => f.BatchDate == date).ToList();

            var paymentViews = batchPayments.Select(p => facts.Payment(p, processor, timings[p.Id])).ToList();
            var shownPayments = ListOf.Capped(paymentViews, cap, $"{processor.Name} payments for {date:yyyy-MM-dd}", out var truncation);
            facts.Truncated(truncation);

            batches.Add(new Batch(
                date,
                shownPayments,
                [.. batchFiles.Select(f => facts.File(f, merchant.Id))],
                [.. payouts.Where(p => p.BatchDate == date).Select(facts.Payout)],
                MissingFile(processor, date, batchPayments, batchFiles, facts),
                PaymentSets(processor, date, batchFiles, facts)));
        }

        var shownBatches = ListOf.Capped(batches, cap, $"{processor.Name} batches", out var batchTruncation);
        facts.Truncated(batchTruncation);
        return new ProcessorBatches(processor.Id, processor.Name, EvidenceIds.Processor(processor.Id), shownBatches);
    }

    // Missing is only ever claimed against payments expected in the batch, so an empty source can't look like an outage.
    private FactRef? MissingFile(Processor processor, DateOnly date, List<Payment> batchPayments, List<SettlementFile> batchFiles, Facts facts)
    {
        if (batchPayments.Count == 0
            || batchFiles.Any(f => f.Status == SettlementFileStatus.Accepted)
            || !SettlementTiming.FileDue(date, processor.Configuration, clock.Now))
        {
            return null;
        }

        var id = EvidenceIds.MissingFile(processor.Id, date);
        facts.Derive(id, $"No accepted {processor.Name} file for {date:yyyy-MM-dd}", [
            .. batchPayments.Select(p => EvidenceIds.Payment(p.Id)),
            EvidenceIds.Processor(processor.Id),
            .. batchFiles.Select(f => EvidenceIds.File(f.Id))]);
        return new FactRef(id);
    }

    private static PaymentSetsView? PaymentSets(Processor processor, DateOnly date, List<SettlementFile> batchFiles, Facts facts)
    {
        var accepted = batchFiles.Where(f => f.Status == SettlementFileStatus.Accepted).OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
        if (accepted.Count < 2)
        {
            return null;
        }

        var relation = SettlementTiming.Relate([.. accepted.Select(f => f.Lines.SelectMany(l => l.PaymentIds).ToList())]);
        var id = EvidenceIds.PaymentSets(processor.Id, date);
        var fileIds = accepted.Select(f => f.Id).ToList();
        facts.Derive(id, $"{processor.Name} files for {date:yyyy-MM-dd} list {ToolJson.Word(relation)} payments", [.. fileIds.Select(EvidenceIds.File)]);
        return new PaymentSetsView(relation, fileIds, id);
    }

    private static string Reason(SettlementDay day) =>
        day.ClosureReason ?? (day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Weekend" : "Not a settlement day");

    /// <summary>The evidence one call builds up: every record read, and each computed fact with its sources.</summary>
    private sealed class Facts(Dictionary<DateOnly, SettlementDay> calendar, DateOnly today)
    {
        public List<EvidenceItem> Records { get; } = [];

        public Dictionary<string, DerivedEvidence> Derived { get; } = new(StringComparer.Ordinal);

        public List<Truncation> Truncations { get; } = [];

        // The calendar was read well past the range; a day outside it is a bug, not a settlement day.
        public bool IsSettlementDay(DateOnly date) =>
            calendar.TryGetValue(date, out var day) ? day.IsSettlementDay : throw new InvalidOperationException($"The calendar wasn't read as far as {date:yyyy-MM-dd}.");

        public void Truncated(Truncation? truncation)
        {
            if (truncation is not null)
            {
                Truncations.Add(truncation);
            }
        }

        public void Derive(string id, string label, IReadOnlyList<string> sources) => Derived.TryAdd(id, new DerivedEvidence(id, label, sources));

        public string NonSettlementDay(DateOnly date)
        {
            var day = calendar[date];
            var calendarId = EvidenceIds.CalendarDay(date);
            if (!Records.Any(r => r.Id == calendarId))
            {
                Records.Add(new RecordEvidence(calendarId, $"{date:yyyy-MM-dd}: {Reason(day)}"));
            }

            var id = EvidenceIds.NonSettlementDay(date);
            Derive(id, $"{date:yyyy-MM-dd} isn't a settlement day ({Reason(day)})", [calendarId]);
            return id;
        }

        public PaymentView Payment(Payment payment, Processor processor, PaymentTiming timing)
        {
            Records.Add(new RecordEvidence(EvidenceIds.Payment(payment.Id), $"Payment {payment.Id}"));
            foreach (var skipped in timing.SkippedDays)
            {
                NonSettlementDay(skipped);
            }

            var id = EvidenceIds.ExpectedBatch(payment.Id);
            Derive(id, $"{payment.Id} belongs in the {timing.ExpectedBatchDate:yyyy-MM-dd} batch", [
                EvidenceIds.Payment(payment.Id),
                EvidenceIds.Processor(processor.Id),
                .. timing.SkippedDays.Select(EvidenceIds.CalendarDay)]);
            return new PaymentView(
                payment.Id, payment.Amount, payment.Currency, payment.CapturedAt, payment.ProcessorCapturedAt, timing.DecidingCaptureTime,
                timing.LocalCaptureTime, timing.AfterCutOff, new DatedFact(timing.ExpectedBatchDate, id), payment.BatchDate, EvidenceIds.Payment(payment.Id));
        }

        public FileView File(SettlementFile file, string merchantId)
        {
            Records.Add(new RecordEvidence(EvidenceIds.File(file.Id), $"Settlement file {file.Id}"));
            var lines = file.Lines.Where(l => l.MerchantId == merchantId).ToList();
            var line = lines.Count == 0 ? null : new MerchantLineView(lines.Sum(l => l.PaymentIds.Count), lines.Sum(l => l.Amount), lines[0].Currency);
            return new FileView(file.Id, file.ReceivedAt, ToolJson.Word(file.Status), line, EvidenceIds.File(file.Id));
        }

        public PayoutView Payout(Payout payout)
        {
            Records.Add(new RecordEvidence(EvidenceIds.Payout(payout.Id), $"Payout {payout.Id}"));
            FactRef? overdue = null;
            if (payout.Status == PayoutStatus.Scheduled && payout.ScheduledFor < today)
            {
                overdue = new FactRef(EvidenceIds.Overdue(payout.Id));
                Derive(overdue.EvidenceId, $"{payout.Id} was due {payout.ScheduledFor:yyyy-MM-dd} and is still scheduled", [EvidenceIds.Payout(payout.Id)]);
            }

            return new PayoutView(payout.Id, ToolJson.Word(payout.Status), payout.ScheduledFor, payout.PaidAt, payout.SourceFileIds, payout.Amount, payout.Currency, EvidenceIds.Payout(payout.Id), overdue);
        }
    }
}
