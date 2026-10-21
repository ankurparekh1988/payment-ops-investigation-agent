namespace PaymentOps.Domain.Data;

/// <summary>
/// A system of record couldn't answer. Every adapter reports failure this way, so callers handle one
/// exception whatever the data source is.
/// </summary>
public sealed class DataSourceUnavailableException(string dataSource, bool isTransient, Exception? innerException = null)
    : Exception($"{dataSource} didn't respond.", innerException)
{
    /// <summary>The port that failed. Named so it doesn't hide <see cref="Exception.Source"/>.</summary>
    public string DataSource { get; } = dataSource;

    public bool IsTransient { get; } = isTransient;
}
