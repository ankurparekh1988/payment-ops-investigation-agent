namespace PaymentOps.Platform.AiRelease;

/// <summary>Prompts from the AI release, by id.</summary>
public interface IPromptProvider
{
    Prompt GetPrompt(string id);
}

public sealed class PromptProvider(AiRelease release) : IPromptProvider
{
    public Prompt GetPrompt(string id) =>
        release.Prompts.TryGetValue(id, out var prompt)
            ? prompt
            : throw new KeyNotFoundException($"Prompt '{id}' isn't in the AI release manifest.");
}
