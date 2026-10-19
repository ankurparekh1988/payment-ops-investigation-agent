using System.ClientModel.Primitives;
using System.Net;
using System.Text;

using Azure.Core;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Answering;
using PaymentOps.Domain.Security;
using PaymentOps.Knowledge.Retrieval;

namespace PaymentOps.Agent.Tests;

internal sealed class FakeChatClient(Func<IList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    public int Calls { get; private set; }

    public ChatOptions? LastOptions { get; private set; }

    public static FakeChatClient Returning(ChatResponse response) => new((_, _, _) => Task.FromResult(response));

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastOptions = options;
        return await respond([.. messages], options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class FakeKnowledgeLookup(Func<KnowledgeAccess, CancellationToken, Task<IReadOnlyList<RetrievedSection>>> retrieve) : IKnowledgeLookup
{
    public int Retrievals { get; private set; }

    public KnowledgeAccess? LastAccess { get; private set; }

    public Func<string, KnowledgeAccess, RetrievedSection?> Find { get; init; } = (_, _) => null;

    public static FakeKnowledgeLookup Returning(params RetrievedSection[] sections) =>
        new((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>(sections));

    public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken)
    {
        Retrievals++;
        LastAccess = access;
        return retrieve(access, cancellationToken);
    }

    public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
        Task.FromResult(Find(sectionKey, access));
}

internal sealed record TestUser(bool IsAuthenticated, bool CanAccessRestrictedKnowledge) : IUserContext
{
    public static TestUser General { get; } = new(true, false);

    public static TestUser Restricted { get; } = new(true, true);

    public static TestUser Anonymous { get; } = new(false, false);

    public string UserId => IsAuthenticated ? "00000000-0000-0000-0000-000000000001" : string.Empty;

    public string DisplayName => "Test";

    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();

    public IReadOnlySet<string> GroupIds { get; } = new HashSet<string>();

    public bool IsInRole(string role) => false;
}

/// <summary>A token that never leaves the test process. Foundry has key authentication disabled, so tests don't use keys either.</summary>
internal sealed class StaticTokenCredential : TokenCredential
{
    public List<string> Scopes { get; } = [];

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        Scopes.AddRange(requestContext.Scopes);
        return new("test-token", DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}

/// <summary>Stands in for the model endpoint over HTTP: records the request and answers with canned JSON.</summary>
internal sealed class StubModelHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
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

/// <summary>A credential whose token request fails, as a managed-identity endpoint can transiently.</summary>
internal sealed class FailingTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new Azure.Identity.AuthenticationFailedException("The managed identity endpoint didn't respond.");

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new Azure.Identity.AuthenticationFailedException("The managed identity endpoint didn't respond.");
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
