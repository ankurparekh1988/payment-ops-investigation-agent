using System.ClientModel.Primitives;
using System.Net;
using System.Text;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Model;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Tools.Knowledge;

namespace PaymentOps.Agent.Tests;

internal sealed class FakeKnowledgeLookup(Func<KnowledgeAccess, CancellationToken, Task<IReadOnlyList<RetrievedSection>>> retrieve) : IKnowledgeLookup
{
    public Func<string, KnowledgeAccess, CancellationToken, Task<RetrievedSection?>> Find { get; init; } = (_, _, _) => Task.FromResult<RetrievedSection?>(null);

    public static FakeKnowledgeLookup Returning(params RetrievedSection[] sections) =>
        new((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>(sections));

    public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken) =>
        retrieve(access, cancellationToken);

    public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
        Find(sectionKey, access, cancellationToken);
}

internal sealed class FakePipelineResponse(int status, string body) : PipelineResponse
{
    private Stream? _stream = new MemoryStream(Encoding.UTF8.GetBytes(body));

    public override int Status => status;

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream
    {
        get => _stream;
        set => _stream = value;
    }

    public override BinaryData Content => BinaryData.FromString(body);

    protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);

    public override void Dispose() => _stream?.Dispose();
}

/// <summary>Keeps every log entry, with its exception, so tests can check what would be written.</summary>
internal sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((formatter(state, exception), exception));
}
