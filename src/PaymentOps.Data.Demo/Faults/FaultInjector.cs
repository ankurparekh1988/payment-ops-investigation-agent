using System.Diagnostics;

using PaymentOps.Domain.Data;

namespace PaymentOps.Data.Demo.Faults;

/// <summary>One port's configured fault, applied the same way to every call so runs repeat.</summary>
internal sealed class FaultInjector(FaultOptions options, string dataSource)
{
    // Empty and Delay still make the real call, so a bad argument is reported as it would be without the fault.
    public async Task<T> RunAsync<T>(Func<Task<T>> call, T empty, CancellationToken cancellationToken)
    {
        switch (options.Mode)
        {
            case FaultMode.Delay:
                await Task.Delay(options.Delay, cancellationToken);
                return await call();
            case FaultMode.Hang:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            case FaultMode.Empty:
                await call();
                return empty;
            case FaultMode.Unavailable:
                throw new DataSourceUnavailableException(dataSource, isTransient: true);
            default:
                throw new UnreachableException($"Fault mode {options.Mode} is never decorated.");
        }
    }
}
