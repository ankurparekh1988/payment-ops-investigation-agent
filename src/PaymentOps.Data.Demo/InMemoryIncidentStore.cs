using System.Globalization;

using PaymentOps.Domain.Incidents;

namespace PaymentOps.Data.Demo;

/// <summary>
/// Tickets held in memory for the demo: lost on restart and not shared between instances. The bound keeps
/// a long-running demo from growing without limit; a real ticket system has no such cap.
/// </summary>
internal sealed class InMemoryIncidentStore(TimeProvider time, int maxTickets = 1000) : IIncidentStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IncidentTicket> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IncidentTicket> _byId = new(StringComparer.Ordinal);

    public Task<IncidentTicket> CreateAsync(string idempotencyKey, IncidentTicketContent content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentNullException.ThrowIfNull(content);

        // One lock: checking the key and the bound, then adding, must be a single step.
        lock (_lock)
        {
            if (_byKey.TryGetValue(idempotencyKey, out var existing))
            {
                return Task.FromResult(existing);
            }

            if (_byId.Count >= maxTickets)
            {
                throw new IncidentStoreFullException();
            }

            var ticket = new IncidentTicket(string.Create(CultureInfo.InvariantCulture, $"INC-{_byId.Count + 1:D4}"), content, time.GetUtcNow());
            _byKey[idempotencyKey] = ticket;
            _byId[ticket.Id] = ticket;
            return Task.FromResult(ticket);
        }
    }

    public Task<IncidentTicket?> FindAsync(string ticketId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_byId.GetValueOrDefault(ticketId));
        }
    }
}
