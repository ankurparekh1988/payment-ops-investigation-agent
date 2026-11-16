using Azure;
using Azure.Data.Tables;

namespace PaymentOps.Web.SmokeTests.Usage;

/// <summary>
/// An in-memory table for single-entity reads and writes as Table Storage does them: each write gets a new ETag, an
/// insert over an existing row fails 409, a replace with a stale ETag fails 412. Reads and writes can be made to fail.
/// One instance stands for one table shared by every app instance.
/// </summary>
internal sealed class FakeEntityTable : TableClient
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Partition, string Row), TableEntity> _rows = [];
    private int _version;

    public Exception? ReadsThrow { get; set; }

    public Exception? WritesThrow { get; set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public TableEntity? Row(string partition, string row)
    {
        lock (_lock)
        {
            return _rows.GetValueOrDefault((partition, row));
        }
    }

    /// <summary>Puts a row as another writer would, with a fresh ETag.</summary>
    public void Put(TableEntity entity)
    {
        lock (_lock)
        {
            entity.ETag = new ETag($"W/\"{++_version}\"");
            _rows[(entity.PartitionKey, entity.RowKey)] = entity;
        }
    }

    public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Reads++;
            if (ReadsThrow is { } thrown)
            {
                throw thrown;
            }

            return Task.FromResult<NullableResponse<T>>(_rows.TryGetValue((partitionKey, rowKey), out var entity)
                ? Response.FromValue((T)(object)Copy(entity), null!)
                : new Missing<T>());
        }
    }

    public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Write();
            var row = (TableEntity)(object)entity;
            if (_rows.ContainsKey((row.PartitionKey, row.RowKey)))
            {
                throw new RequestFailedException(409, "exists");
            }

            Put(Copy(row));
            return Task.FromResult<Response>(null!);
        }
    }

    public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Write();
            var row = (TableEntity)(object)entity;
            if (!_rows.TryGetValue((row.PartitionKey, row.RowKey), out var current))
            {
                throw new RequestFailedException(404, "missing");
            }

            if (ifMatch != ETag.All && ifMatch != current.ETag)
            {
                throw new RequestFailedException(412, "changed");
            }

            Put(Copy(row));
            return Task.FromResult<Response>(null!);
        }
    }

    private void Write()
    {
        Writes++;
        if (WritesThrow is { } thrown)
        {
            throw thrown;
        }
    }

    private static TableEntity Copy(TableEntity entity) => new(entity) { ETag = entity.ETag };

    private sealed class Missing<T> : NullableResponse<T>
    {
        public override bool HasValue => false;

        public override T Value => throw new InvalidOperationException("No entity.");

        public override Response GetRawResponse() => null!;
    }
}
