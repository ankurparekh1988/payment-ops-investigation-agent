using System.ClientModel.Primitives;

namespace PaymentOps.Agent.Telemetry;

/// <summary>
/// Counts every request sent to Content Safety, retries included, by operation. A request that never answered may
/// still have been billed, so the turn's usage becomes incomplete.
/// </summary>
internal sealed class RequestCountingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var service = Service(request.RequestUri?.AbsolutePath);
        TurnUsage.RecordHttpRequest(service);
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch
        {
            TurnUsage.RecordIncomplete($"{service} outcome unknown");
            throw;
        }
    }

    /// <summary>The service an operation path belongs to, as metrics and the turn's usage name it.</summary>
    internal static string Service(string? path) =>
        path?.EndsWith("text:shieldPrompt", StringComparison.Ordinal) == true ? "prompt_shields"
        : path?.EndsWith("text:analyze", StringComparison.Ordinal) == true ? "text_analysis"
        : "content_safety";
}

/// <summary>
/// Counts every attempt the model client sends. Added per try, beneath the SDK's retry policy, so retries are seen
/// without changing the transport or the retry behaviour.
/// </summary>
internal sealed class RequestCountingPolicy(string deployment) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        TurnUsage.RecordHttpRequest("model", deployment);
        try
        {
            ProcessNext(message, pipeline, currentIndex);
        }
        catch
        {
            TurnUsage.RecordIncomplete("model outcome unknown");
            throw;
        }
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        TurnUsage.RecordHttpRequest("model", deployment);
        try
        {
            await ProcessNextAsync(message, pipeline, currentIndex);
        }
        catch
        {
            TurnUsage.RecordIncomplete("model outcome unknown");
            throw;
        }
    }
}
