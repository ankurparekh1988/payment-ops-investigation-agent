using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Azure;
using Azure.Data.Tables;

namespace PaymentOps.Web.Usage;

/// <summary>
/// Daily counts in a table, one partition per UTC day: a row per person and one for the total. A reservation reads
/// both, refuses without writing if either is at its limit, and otherwise updates both in one transaction conditioned
/// on what it read, so neither limit can be passed and one row never changes without the other. A conflicting
/// reservation reads again; storage that fails, or keeps conflicting, refuses.
/// </summary>
internal sealed class TableUsageCounter(TableClient table, int maxAttempts = TableUsageCounter.DefaultMaxAttempts, Func<int, TimeSpan>? backoff = null) : IUsageCounter
{
    public const int DefaultMaxAttempts = 5;
    public const string TotalRow = "total";
    private const string CountProperty = "Count";

    private readonly Func<int, TimeSpan> _backoff = backoff ?? (attempt => TimeSpan.FromMilliseconds(Random.Shared.Next(20, 200) * attempt));

    /// <summary>A person's row: a hash of their id, so the table holds no user id.</summary>
    public static string UserRow(string userId) =>
        "u-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..32];

    public async Task<UsageReservation> TryReserveAsync(string userId, DateOnly day, int perUserLimit, int totalLimit, CancellationToken cancellationToken)
    {
        var partition = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var userRow = UserRow(userId);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var userRead = ReadAsync(partition, userRow, cancellationToken);
                var totalRead = ReadAsync(partition, TotalRow, cancellationToken);
                await Task.WhenAll(userRead, totalRead);
                var (user, total) = (userRead.Result, totalRead.Result);
                if (user.Count >= perUserLimit)
                {
                    return UsageReservation.UserLimitReached;
                }

                if (total.Count >= totalLimit)
                {
                    return UsageReservation.TotalLimitReached;
                }

                await table.SubmitTransactionAsync([Increment(partition, userRow, user), Increment(partition, TotalRow, total)], cancellationToken);
                return UsageReservation.Admitted;
            }
            catch (TableTransactionFailedException ex) when (ex.Status is 409 or 412)
            {
                // Another reservation counted since this one read; read again.
                if (attempt < maxAttempts)
                {
                    await Task.Delay(_backoff(attempt), cancellationToken);
                }
            }
            // Any other failure is an outage, whatever form it takes: a service response, retries exhausted, a network
            // timeout or a sign-in failure. Only the caller's own cancellation stays a cancellation.
            catch (Exception ex) when (ex is not UsageUnavailableException && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                throw new UsageUnavailableException("Usage couldn't be read or written.", ex);
            }
        }

        throw new UsageUnavailableException("Usage kept changing while it was being reserved.");
    }

    private async Task<(int Count, ETag? ETag)> ReadAsync(string partition, string row, CancellationToken cancellationToken)
    {
        var entity = await table.GetEntityIfExistsAsync<TableEntity>(partition, row, cancellationToken: cancellationToken);
        if (!entity.HasValue)
        {
            return (0, null);
        }

        // A row that exists but holds no usable count or ETag isn't a fresh day: zero would reset the limits, and an
        // update without its ETag would overwrite whatever is there.
        if (entity.Value!.GetInt32(CountProperty) is not { } count || count < 0 || entity.Value.ETag == default)
        {
            throw new UsageUnavailableException("A usage row is malformed.");
        }

        return (count, entity.Value.ETag);
    }

    // A row that didn't exist is inserted, which fails if another reservation inserted it first.
    private static TableTransactionAction Increment(string partition, string row, (int Count, ETag? ETag) read)
    {
        var entity = new TableEntity(partition, row) { [CountProperty] = read.Count + 1 };
        return read.ETag is { } etag
            ? new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity, etag)
            : new TableTransactionAction(TableTransactionActionType.Add, entity);
    }
}
