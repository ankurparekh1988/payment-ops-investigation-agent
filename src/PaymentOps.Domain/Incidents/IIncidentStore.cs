namespace PaymentOps.Domain.Incidents;

/// <summary>Where approved incident tickets are created. A real ticket system replaces the demo adapter behind this port.</summary>
public interface IIncidentStore
{
    /// <summary>
    /// Creates the ticket, or returns the one already created for <paramref name="idempotencyKey"/> and creates nothing.
    /// Throws <see cref="IncidentStoreFullException"/> when the store refuses new tickets.
    /// </summary>
    Task<IncidentTicket> CreateAsync(string idempotencyKey, IncidentTicketContent content, CancellationToken cancellationToken);

    /// <summary>The ticket, or null if there's no ticket with that id.</summary>
    Task<IncidentTicket?> FindAsync(string ticketId, CancellationToken cancellationToken);
}
