using Azure;
using Azure.Data.Tables;

namespace PaymentOps.Web.Usage;

/// <summary>
/// The pause as one row in the usage table. Each instance caches it for <see cref="CacheLifetime"/>, so another
/// instance sees a change within that time and the instance that made it at once. A row that can't be read fails
/// closed; a write is conditioned on the version it was decided from, so a lost race reports the winner.
/// </summary>
internal sealed class TablePauseStore(TableClient table, TimeProvider time) : IPauseStore
{
    public const string Partition = "control";
    public const string RowKey = "pause";
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    // Timestamped on the monotonic clock, so setting the wall clock back can't stretch the lifetime.
    private (PauseState State, long ReadAt)? _cache;

    // Counts this instance's writes, so a read that started before one can't put the older state back.
    private int _writes;

    public Task<PauseState> ReadAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_cache is { } cached && time.GetElapsedTime(cached.ReadAt) < CacheLifetime)
            {
                return Task.FromResult(cached.State);
            }
        }

        return ReadFreshAsync(cancellationToken);
    }

    public async Task<PauseState> ReadFreshAsync(CancellationToken cancellationToken)
    {
        int writesBefore;
        lock (_lock)
        {
            writesBefore = _writes;
        }

        PauseState state;
        try
        {
            var entity = await table.GetEntityIfExistsAsync<TableEntity>(Partition, RowKey, cancellationToken: cancellationToken);
            state = entity.HasValue ? FromEntity(entity.Value!) : PauseState.Running;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            Forget();
            throw ex as PauseUnavailableException ?? new PauseUnavailableException("The pause couldn't be read.", ex);
        }

        lock (_lock)
        {
            // A write landed while this read was in flight: what it wrote is newer than what was read.
            if (_writes != writesBefore && _cache is { } newer)
            {
                return newer.State;
            }

            _cache = (state, time.GetTimestamp());
            return state;
        }
    }

    public async Task<PauseWrite> WriteAsync(PauseState next, string? expectedVersion, CancellationToken cancellationToken)
    {
        var entity = new TableEntity(Partition, RowKey)
        {
            ["Paused"] = next.Paused,
            ["ChangedBy"] = next.ChangedBy,
            ["ChangedByName"] = next.ChangedByName,
            ["ChangedAt"] = next.ChangedAt,
            ["Reason"] = next.Reason,
        };

        try
        {
            if (expectedVersion is null)
            {
                await table.AddEntityAsync(entity, cancellationToken);
            }
            else
            {
                await table.UpdateEntityAsync(entity, new ETag(expectedVersion), TableUpdateMode.Replace, cancellationToken);
            }
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412)
        {
            // Someone else changed it since it was read: report what's there now.
            try
            {
                return new PauseWrite.Conflict(await ReadFreshAsync(cancellationToken));
            }
            catch (PauseUnavailableException)
            {
                return new PauseWrite.Failed();
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Nothing is known to have changed; the next read goes to the table.
            Forget();
            return new PauseWrite.Failed();
        }

        lock (_lock)
        {
            _writes++;
            _cache = (next, time.GetTimestamp());
        }

        return new PauseWrite.Written(next);
    }

    // A row without a readable flag isn't "running": refusing is safer than guessing.
    private static PauseState FromEntity(TableEntity entity) =>
        entity.GetBoolean("Paused") is { } paused
            ? new PauseState(paused, entity.GetString("ChangedBy"), entity.GetString("ChangedByName"), entity.GetDateTimeOffset("ChangedAt"), entity.GetString("Reason"))
            {
                Version = entity.ETag == default ? null : entity.ETag.ToString(),
            }
            : throw new PauseUnavailableException("The pause row is malformed.");

    private void Forget()
    {
        lock (_lock)
        {
            _cache = null;
        }
    }
}
