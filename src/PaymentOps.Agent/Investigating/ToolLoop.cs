using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Investigating;

internal static class ToolLoop
{
    /// <summary>Ends the framework's tool loop after the current call; no further model request is made.</summary>
    public static void End()
    {
        if (FunctionInvokingChatClient.CurrentContext is { } context)
        {
            context.Terminate = true;
        }
    }
}
