using System.ClientModel;
using System.Text.Json;

namespace PaymentOps.Agent.Answering;

/// <summary>Classifies model-call failures for the result and for safe diagnostics.</summary>
internal static class ModelFailure
{
    /// <summary>
    /// A failure of the model service rather than of our code. The SDK reports a request that failed
    /// on every retry as an aggregate of the attempts.
    /// </summary>
    public static bool IsServiceFailure(Exception exception) =>
        exception is ClientResultException or HttpRequestException
        || (exception is AggregateException retries && retries.InnerExceptions.All(inner => inner is ClientResultException));

    /// <summary>The failed attempt to report: the exception itself, or the last attempt of a retried request.</summary>
    public static ClientResultException? LastAttempt(Exception exception) =>
        exception as ClientResultException ?? (exception as AggregateException)?.InnerExceptions.OfType<ClientResultException>().LastOrDefault();

    /// <summary>Azure can reject the request itself, before any completion, when content filtering trips.</summary>
    public static bool IsContentFilterRejection(Exception exception) =>
        exception is ClientResultException { Status: 400 } client && ErrorCode(client) == "content_filter";

    public static string? ErrorCode(ClientResultException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var body = exception.GetRawResponse()?.Content;
            if (body is null)
            {
                return null;
            }

            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? RequestId(ClientResultException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var headers = exception.GetRawResponse()?.Headers;
            return headers is null ? null
                : headers.TryGetValue("apim-request-id", out var apim) ? apim
                : headers.TryGetValue("x-request-id", out var request) ? request
                : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
