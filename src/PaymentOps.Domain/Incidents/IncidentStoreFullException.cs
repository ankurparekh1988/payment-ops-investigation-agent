namespace PaymentOps.Domain.Incidents;

/// <summary>The store accepts no new tickets. Tickets already created stay findable.</summary>
public sealed class IncidentStoreFullException : Exception
{
    public IncidentStoreFullException()
        : base("The incident store accepts no new tickets.")
    {
    }

    public IncidentStoreFullException(string message)
        : base(message)
    {
    }

    public IncidentStoreFullException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
