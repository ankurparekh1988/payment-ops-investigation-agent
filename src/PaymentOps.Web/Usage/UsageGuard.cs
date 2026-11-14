using Microsoft.Extensions.Options;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Domain.Security;

namespace PaymentOps.Web.Usage;

/// <summary>
/// Admits an investigation only while investigations aren't paused and the person and everyone together are under
/// today's limits. Nothing is reserved for a refusal; an admitted attempt counts whatever happens next, because
/// screening has already been paid for. Usage that can't be checked refuses: the limits are a cost control.
/// </summary>
internal sealed partial class UsageGuard(
    IInvestigationService inner,
    IUsageCounter counter,
    IUserContext user,
    IOptionsMonitor<UsageOptions> options,
    TimeProvider clock,
    ILogger<UsageGuard> logger) : IInvestigationService
{
    public ConversationStatus Conversation => inner.Conversation;

    public async Task<AssessmentResult> InvestigateAsync(string question, IProgress<InvestigationProgress>? progress, CancellationToken cancellationToken)
    {
        var limits = options.CurrentValue;
        if (limits.Paused)
        {
            return Refuse(NotAssessedReason.Paused);
        }

        UsageReservation reservation;
        try
        {
            reservation = await counter.TryReserveAsync(user.UserId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), limits.DailyLimitPerUser, limits.DailyLimitTotal, cancellationToken);
        }
        catch (UsageUnavailableException ex)
        {
            // The cause's type only: who asked isn't logged.
            UsageUnavailable(logger, ex.InnerException?.GetType().Name ?? nameof(UsageUnavailableException));
            return Refuse(NotAssessedReason.UsageUnavailable);
        }

        return reservation == UsageReservation.Admitted
            ? await inner.InvestigateAsync(question, progress, cancellationToken)
            : Refuse(NotAssessedReason.DailyLimitReached);
    }

    public void StartNew() => inner.StartNew();

    public Task<SourceResult> GetSourceAsync(string sectionKey, CancellationToken cancellationToken) => inner.GetSourceAsync(sectionKey, cancellationToken);

    private static AssessmentResult.NotAssessed Refuse(NotAssessedReason reason)
    {
        AgentTelemetry.RecordRejected(reason);
        return new AssessmentResult.NotAssessed(reason);
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Warning, Message = "Investigation refused: today's usage couldn't be checked ({Cause})")]
    private static partial void UsageUnavailable(ILogger logger, string cause);
}
