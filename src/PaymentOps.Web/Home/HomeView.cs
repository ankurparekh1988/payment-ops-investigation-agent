using PaymentOps.Web.Usage;

namespace PaymentOps.Web.Home;

/// <summary>
/// What the home page shows, built for one person by role. A section their role doesn't get is null, not empty, so
/// the page can't render what the overview never fetched.
/// </summary>
/// <param name="Usage">The person's usage today; null when it couldn't be read.</param>
/// <param name="AsOf">The demo data's "now", which the processor and settlement sections are read against.</param>
/// <param name="ShowApprovals">Engineers and above: this session's tickets waiting for approval.</param>
/// <param name="Tickets">Engineers and above: the newest tickets.</param>
/// <param name="Operations">Admins only.</param>
public sealed record HomeView(
    DateTimeOffset AsOf,
    IReadOnlyList<ProcessorIncidentItem> Processors,
    IReadOnlyList<AttentionItem> Attention,
    UsageToday? Usage,
    bool ShowApprovals,
    IReadOnlyList<TicketItem>? Tickets,
    OperationsView? Operations,
    bool RestrictedKnowledge);

public sealed record ProcessorIncidentItem(string ProcessorName, string Summary, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt);

public enum AttentionKind
{
    HeldPayout,
    RejectedFile,
}

/// <param name="Subject">The merchant for a held payout, the processor for a rejected file.</param>
/// <param name="Question">A fixed question built from the record, handed to Investigate without running it.</param>
public sealed record AttentionItem(AttentionKind Kind, string Subject, string ProcessorName, DateOnly BatchDate, string Question);

/// <summary>
/// A ticket as the team list shows it: facts from typed data only. The title and summary are model-written and may draw on
/// restricted runbooks, so they stay on the owner's card.
/// </summary>
public sealed record TicketItem(string Id, string ProcessorId, IReadOnlyList<string> MerchantIds, DateTimeOffset CreatedAt);

/// <summary>The person's investigations started today against their daily limit.</summary>
public sealed record UsageToday(int Used, int Limit);

/// <param name="TotalToday">Everyone's investigations today; null when usage couldn't be read.</param>
/// <param name="PausedByConfiguration">The deployment's setting pauses investigations, whatever the switch says.</param>
/// <param name="Pause">The Admin switch and its last change; null when it couldn't be read.</param>
public sealed record OperationsView(
    int? TotalToday,
    int TotalLimit,
    int PerUserLimit,
    bool PausedByConfiguration,
    PauseState? Pause,
    Uri? WorkbookUrl,
    Uri? AlertsUrl);
