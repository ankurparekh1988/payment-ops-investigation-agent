using System.Globalization;

namespace PaymentOps.Web.Usage;

/// <summary>The pause held in memory, for Development without a table: one instance, reset with the process.</summary>
internal sealed class InMemoryPauseStore : IPauseStore
{
    private readonly Lock _lock = new();
    private PauseState _state = PauseState.Running;
    private int _version;

    public PauseState Current
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public Task<PauseState> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<PauseState> ReadFreshAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task<PauseWrite> WriteAsync(PauseState next, string? expectedVersion, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (expectedVersion != _state.Version)
            {
                return Task.FromResult<PauseWrite>(new PauseWrite.Conflict(_state));
            }

            _state = next with { Version = (++_version).ToString(CultureInfo.InvariantCulture) };
            return Task.FromResult<PauseWrite>(new PauseWrite.Written(_state));
        }
    }
}
