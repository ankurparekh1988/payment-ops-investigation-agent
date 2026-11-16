using Microsoft.Extensions.Logging;

namespace PaymentOps.Web.SmokeTests;

/// <summary>Keeps each entry's event id and its message with every structured value, to check what a log holds.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(EventId EventId, string Text)> Entries { get; } = [];

    public IEnumerable<string> Messages => Entries.Select(e => e.Text);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((eventId, formatter(state, exception) + " " + string.Join(' ', (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(p => p.Value) ?? [])));
}
