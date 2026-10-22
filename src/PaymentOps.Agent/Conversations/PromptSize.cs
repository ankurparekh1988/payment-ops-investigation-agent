using Microsoft.Extensions.AI;

namespace PaymentOps.Agent.Conversations;

/// <summary>
/// The application's estimate of request size, used for the history budget and checked by the live run against
/// the tokens the service counted. Deliberately conservative: three characters a token.
/// </summary>
public static class PromptSize
{
    public const int CharsPerToken = 3;

    public static int Tokens(int chars) => (chars + CharsPerToken - 1) / CharsPerToken;

    /// <summary>A tool definition as the request carries it: name, description and parameter schema.</summary>
    public static int DefinitionChars(AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);

        return function.Name.Length + function.Description.Length + function.JsonSchema.GetRawText().Length;
    }
}
