using Azure;
using Azure.Data.Tables;

namespace PaymentOps.Web.SmokeTests.Usage;

/// <summary>
/// An in-memory table that behaves as Table Storage does for what the counter uses: point reads with ETags, and
/// entity-group transactions that apply every action or none, failing 409 on an insert that exists and 412 on an
/// update whose ETag no longer matches. Reads and transactions can be made to fail.
/// </summary>
internal sealed class FakeTableClient : TableClient
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Partition, string Row), (int Count, ETag ETag)> _rows = [];
    private readonly Dictionary<(string Partition, string Row), TableEntity> _planted = [];
    private int _version;

    /// <summary>Thrown by every read.</summary>
    public Exception? ReadsThrow { get; set; }

    public int? FailTransactionsWith { get; set; }

    public int Transactions { get; private set; }

    public int Count(string partition, string row)
    {
        lock (_lock)
        {
            return _rows.TryGetValue((partition, row), out var entry) ? entry.Count : 0;
        }
    }

    /// <summary>Stores a row exactly as given, whatever it holds, as a corrupted or hand-edited table would.</summary>
    public void Plant(TableEntity entity)
    {
        lock (_lock)
        {
            _planted[(entity.PartitionKey, entity.RowKey)] = entity;
        }
    }

    /// <summary>A planted row as it is now: the same entity unless a transaction replaced it.</summary>
    public TableEntity Planted(string partition, string row)
    {
        lock (_lock)
        {
            return _planted[(partition, row)];
        }
    }

    public IReadOnlyDictionary<(string Partition, string Row), int> Rows
    {
        get
        {
            lock (_lock)
            {
                return _rows.ToDictionary(r => r.Key, r => r.Value.Count);
            }
        }
    }

    public override async Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
    {
        // Let concurrent reservations interleave between their read and their write, as they would against a service.
        await Task.Yield();
        if (ReadsThrow is { } thrown)
        {
            throw thrown;
        }

        lock (_lock)
        {
            if (_planted.TryGetValue((partitionKey, rowKey), out var planted))
            {
                return Response.FromValue((T)(object)planted, null!);
            }

            if (!_rows.TryGetValue((partitionKey, rowKey), out var entry))
            {
                return new Missing<T>();
            }

            var entity = new TableEntity(partitionKey, rowKey) { ["Count"] = entry.Count, ETag = entry.ETag };
            return Response.FromValue((T)(object)entity, null!);
        }
    }

    public override async Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var actions = transactionActions.ToList();
        lock (_lock)
        {
            Transactions++;
            if (FailTransactionsWith is { } failure)
            {
                throw Failed(failure);
            }

            foreach (var action in actions)
            {
                var key = (action.Entity.PartitionKey, action.Entity.RowKey);
                if (_planted.TryGetValue(key, out var planted))
                {
                    // As the service would: an unconditional replace succeeds, so a test sees the damage.
                    if (action.ActionType != TableTransactionActionType.UpdateReplace || (action.ETag != default && action.ETag != ETag.All && action.ETag != planted.ETag))
                    {
                        throw Failed(action.ActionType == TableTransactionActionType.Add ? 409 : 412);
                    }

                    continue;
                }

                var exists = _rows.TryGetValue(key, out var current);
                if (action.ActionType == TableTransactionActionType.Add && exists)
                {
                    throw Failed(409);
                }

                if (action.ActionType == TableTransactionActionType.UpdateReplace && (!exists || current.ETag != action.ETag))
                {
                    throw Failed(exists ? 412 : 404);
                }
            }

            foreach (var action in actions)
            {
                if (_planted.ContainsKey((action.Entity.PartitionKey, action.Entity.RowKey)))
                {
                    _planted[(action.Entity.PartitionKey, action.Entity.RowKey)] = (TableEntity)action.Entity;
                    continue;
                }

                var count = Convert.ToInt32(((TableEntity)action.Entity)["Count"], System.Globalization.CultureInfo.InvariantCulture);
                _rows[(action.Entity.PartitionKey, action.Entity.RowKey)] = (count, new ETag($"W/\"{++_version}\""));
            }
        }

        return Response.FromValue<IReadOnlyList<Response>>([], null!);
    }

    private static TableTransactionFailedException Failed(int status) => new(new RequestFailedException(status, $"transaction failed {status}"));

    private sealed class Missing<T> : NullableResponse<T>
    {
        public override bool HasValue => false;

        public override T Value => throw new InvalidOperationException("No entity.");

        public override Response GetRawResponse() => null!;
    }
}
