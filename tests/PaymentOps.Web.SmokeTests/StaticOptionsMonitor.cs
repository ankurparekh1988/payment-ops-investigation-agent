using Microsoft.Extensions.Options;

namespace PaymentOps.Web.SmokeTests;

/// <summary>Options that never change, for code that reads them through a monitor.</summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
