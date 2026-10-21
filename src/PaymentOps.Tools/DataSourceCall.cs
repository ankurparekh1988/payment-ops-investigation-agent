using PaymentOps.Domain.Data;

namespace PaymentOps.Tools;

/// <summary>The names data sources go by in results and gaps, independent of any adapter's own names.</summary>
public static class DataSources
{
    public const string Merchants = "Merchant data";
    public const string Settlement = "Settlement data";
    public const string Processors = "Processor data";
    public const string ErrorEvents = "Error events";
    public const string Knowledge = "Knowledge search";
}

/// <summary>
/// One call to a system of record, retried once when the failure is transient. The retry lives here so the
/// model is never told to retry: by the time it sees "unavailable", retrying has already been done.
/// </summary>
internal static class DataSourceCall
{
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, string source, TimeSpan backoff, CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (DataSourceUnavailableException ex) when (ex.IsTransient)
        {
            await Task.Delay(backoff, cancellationToken);
        }
        catch (DataSourceUnavailableException)
        {
            throw Unavailable(source);
        }

        try
        {
            return await call(cancellationToken);
        }
        catch (DataSourceUnavailableException)
        {
            throw Unavailable(source);
        }
    }

    private static ToolFailedException Unavailable(string source) =>
        new(new ToolResult.Failed(ToolErrorCode.Unavailable, $"{source} is unavailable.", source));
}
