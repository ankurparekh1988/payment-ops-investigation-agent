using Microsoft.Extensions.Options;

using PaymentOps.Domain.Incidents;
using PaymentOps.Domain.Investigation;
using PaymentOps.Domain.Processors;
using PaymentOps.Domain.Security;
using PaymentOps.Domain.Settlement;
using PaymentOps.Web.Usage;

namespace PaymentOps.Web.Home;

/// <summary>
/// Builds the home page for the signed-in person, layered by role: everyone gets processor incidents, settlements
/// needing attention and their own usage; Engineers add approvals and recent tickets; Admins add operations. Demo
/// data is read as of the demo clock, so the page agrees with what an investigation finds; usage uses today's date.
/// </summary>
internal sealed class HomeOverview(
    IUserContext user,
    IProcessorData processors,
    ISettlementData settlement,
    IIncidentStore tickets,
    IInvestigationClock demoClock,
    TimeProvider wallClock,
    IOptionsMonitor<UsageOptions> usage,
    IOptions<UiOptions> ui,
    IPauseStore pause,
    IUsageCounter? counter = null)
{
    public const int AttentionLimit = 10;
    public const int TicketLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public async Task<HomeView> LoadAsync(CancellationToken cancellationToken)
    {
        var engineer = user.IsInRole(OpsRoles.Engineer);
        var admin = user.IsInRole(OpsRoles.Admin);
        var asOf = demoClock.Now;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        var incidents = await processors.GetAllIncidentsAsync(asOf - Window, asOf, cancellationToken);
        var processorItems = new List<ProcessorIncidentItem>();
        foreach (var incident in incidents.OrderByDescending(i => i.StartedAt))
        {
            processorItems.Add(new ProcessorIncidentItem(await NameAsync(incident.ProcessorId), incident.Summary, incident.StartedAt, incident.ResolvedAt));
        }

        var attention = await AttentionAsync(demoClock.Today, NameAsync, cancellationToken);
        var (usageToday, totalToday, usageUnavailable) = await UsageAsync(cancellationToken);
        var limits = usage.CurrentValue;

        return new HomeView(
            asOf,
            processorItems,
            attention,
            usageToday,
            usageUnavailable,
            ShowApprovals: engineer,
            Tickets: engineer ? await RecentTicketsAsync(cancellationToken) : null,
            Operations: admin
                ? new OperationsView(totalToday, limits.DailyLimitTotal, limits.DailyLimitPerUser, limits.Paused, await PauseAsync(cancellationToken), ui.Value.WorkbookUrl, ui.Value.AlertsUrl)
                : null,
            RestrictedKnowledge: user.CanAccessRestrictedKnowledge);

        async Task<string> NameAsync(string processorId)
        {
            if (!names.TryGetValue(processorId, out var name))
            {
                name = (await processors.FindAsync(processorId, cancellationToken))?.Name ?? processorId;
                names[processorId] = name;
            }

            return name;
        }
    }

    // Batch dates from seven days before the demo's today up to and including it, as the incident window does; newest first.
    private async Task<IReadOnlyList<AttentionItem>> AttentionAsync(DateOnly today, Func<string, Task<string>> name, CancellationToken cancellationToken)
    {
        var (from, to) = (today.AddDays(-(int)Window.TotalDays), today.AddDays(1));
        var held = await settlement.GetHeldPayoutsAsync(from, to, cancellationToken);
        var rejected = await settlement.GetRejectedFilesAsync(from, to, cancellationToken);

        var items = new List<AttentionItem>();
        foreach (var payout in held)
        {
            items.Add(new AttentionItem(AttentionKind.HeldPayout, payout.MerchantId, await name(payout.ProcessorId), payout.BatchDate,
                AttentionQuestion.HeldPayout(payout.MerchantId, payout.BatchDate)));
        }

        foreach (var file in rejected)
        {
            items.Add(new AttentionItem(AttentionKind.RejectedFile, file.ProcessorId, await name(file.ProcessorId), file.BatchDate,
                AttentionQuestion.RejectedFile(file.ProcessorId, file.BatchDate)));
        }

        return [.. items
            .OrderByDescending(i => i.BatchDate).ThenBy(i => i.Kind).ThenBy(i => i.Subject, StringComparer.Ordinal)
            .Take(AttentionLimit)];
    }

    // Reads, never reserves. Usage that can't be read is reported as such rather than shown as zero.
    private async Task<(UsageToday? Usage, int? Total, bool Unavailable)> UsageAsync(CancellationToken cancellationToken)
    {
        if (counter is null)
        {
            return (null, null, true);
        }

        try
        {
            var (mine, total) = await counter.ReadAsync(user.UserId, DateOnly.FromDateTime(wallClock.GetUtcNow().UtcDateTime), cancellationToken);
            return (new UsageToday(mine, usage.CurrentValue.DailyLimitPerUser), total, false);
        }
        catch (UsageUnavailableException)
        {
            return (null, null, true);
        }
    }

    private async Task<PauseState?> PauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await pause.ReadAsync(cancellationToken);
        }
        catch (PauseUnavailableException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<TicketItem>> RecentTicketsAsync(CancellationToken cancellationToken) =>
        [.. (await tickets.GetRecentAsync(TicketLimit, cancellationToken))
            .Select(t => new TicketItem(t.Id, t.Content.Title, t.Content.ProcessorId, t.Content.MerchantIds, t.CreatedAt))];
}
