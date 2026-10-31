using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Model;

/// <summary>The model's content filter withheld its completion: the turn ends as blocked.</summary>
internal sealed class ContentFilteredException() : Exception("The model's completion was withheld by the content filter.");

internal static class ContentFilter
{
    /// <summary>
    /// The model, ending the turn on any filtered completion, wherever in the turn it comes. A filtered completion
    /// carries no tool call, so without this the turn would read as an unverified assessment, not a block.
    /// </summary>
    public static IChatClient Checked(IChatClient model) =>
        model.AsBuilder().Use(
            async (messages, options, inner, cancellationToken) =>
            {
                var response = await inner.GetResponseAsync(messages, options, cancellationToken);
                return response.FinishReason == ChatFinishReason.ContentFilter ? throw new ContentFilteredException() : response;
            },
            null).Build();
}
