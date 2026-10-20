using System.Diagnostics;

using PaymentOps.Domain.Data;

namespace PaymentOps.Data.Demo.Faults;

internal static class FaultInjector
{
    // Empty and Delay still make the real call, so a bad argument is reported as it would be without the fault.
    public static async Task<T> RunAsync<T>(FaultOptions options, string dataSource, Func<Task<T>> call, T empty, CancellationToken cancellationToken)
    {
        switch (options.Mode)
        {
            case FaultMode.None:
                return await call();
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
                throw new UnreachableException($"Unknown fault mode {options.Mode}.");
        }
    }
}
