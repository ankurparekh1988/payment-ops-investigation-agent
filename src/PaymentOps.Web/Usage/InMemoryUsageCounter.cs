namespace PaymentOps.Web.Usage;

/// <summary>The same reservation rules held in memory, for Development without a table: counts reset with the process.</summary>
internal sealed class InMemoryUsageCounter : IUsageCounter
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(DateOnly Day, string User), int> _users = [];
    private readonly Dictionary<DateOnly, int> _totals = [];

    public Task<UsageReservation> TryReserveAsync(string userId, DateOnly day, int perUserLimit, int totalLimit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var user = _users.GetValueOrDefault((day, userId));
            var total = _totals.GetValueOrDefault(day);
            if (user >= perUserLimit)
            {
                return Task.FromResult(UsageReservation.UserLimitReached);
            }

            if (total >= totalLimit)
            {
                return Task.FromResult(UsageReservation.TotalLimitReached);
            }

            _users[(day, userId)] = user + 1;
            _totals[day] = total + 1;
            return Task.FromResult(UsageReservation.Admitted);
        }
    }

    public Task<(int User, int Total)> ReadAsync(string userId, DateOnly day, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult((_users.GetValueOrDefault((day, userId)), _totals.GetValueOrDefault(day)));
        }
    }
}
