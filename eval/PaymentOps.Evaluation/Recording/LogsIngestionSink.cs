using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Ingestion;

using PaymentOps.Evaluation.Running;

namespace PaymentOps.Evaluation.Recording;

/// <summary>Where recorded evaluation rows go.</summary>
internal interface IEvaluationSink
{
    Task WriteAsync(IReadOnlyList<EvaluationRow> rows, CancellationToken cancellationToken);
}

/// <summary>
/// Writes rows to the AiEvaluation_CL table through the Logs Ingestion API, signed in with Entra; the identity needs
/// only Monitoring Metrics Publisher on the one data collection rule.
/// </summary>
internal sealed class LogsIngestionSink(Uri ingestionEndpoint, string ruleImmutableId, TokenCredential credential) : IEvaluationSink
{
    public const string Stream = "Custom-AiEvaluation_CL";

    private readonly LogsIngestionClient _client = new(ingestionEndpoint, credential);

    public async Task WriteAsync(IReadOnlyList<EvaluationRow> rows, CancellationToken cancellationToken)
    {
        try
        {
            await _client.UploadAsync(ruleImmutableId, Stream, rows, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new CouldNotEvaluateException($"Log ingestion was refused ({ex.Status} {ex.ErrorCode}); the publisher needs Monitoring Metrics Publisher on the evaluation rule.");
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or HttpRequestException or AggregateException)
        {
            throw new CouldNotEvaluateException($"Log ingestion couldn't be reached or signed in to: {ex.Message}");
        }
    }
}
