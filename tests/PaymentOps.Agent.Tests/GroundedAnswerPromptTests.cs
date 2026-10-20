using Microsoft.Extensions.AI;

using PaymentOps.Agent.Answering;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Tests;

public sealed class GroundedAnswerPromptTests
{
    [Fact]
    public void The_question_and_each_section_are_delimited_with_their_ids_and_without_classification()
    {
        var prompt = AiReleaseLoader.Load(new EmbeddedAiReleaseSource()).Prompts[GroundedAnswerPrompt.PromptId];
        var registry = EvidenceRegistry.From([EvidenceRegistryTests.Section("a", "Missing settlement batch", "Missing settlement batch > Act") with { Classification = "restricted" }]);

        var messages = GroundedAnswerPrompt.Build(prompt, "Where is the batch?", registry);

        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("[S1]", messages[0].Text, StringComparison.Ordinal);
        var user = messages[1].Text;
        Assert.Contains("<question>\nWhere is the batch?\n</question>", user, StringComparison.Ordinal);
        Assert.Contains("<section id=\"S1\">", user, StringComparison.Ordinal);
        Assert.Contains("Missing settlement batch > Act", user, StringComparison.Ordinal);
        Assert.DoesNotContain("restricted", user, StringComparison.OrdinalIgnoreCase);
    }
}
