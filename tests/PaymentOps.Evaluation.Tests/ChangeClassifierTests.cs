using PaymentOps.Evaluation.Classification;
using PaymentOps.Evaluation.Commands;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

public sealed class ChangeClassifierTests
{
    private static ChangeClassification Classify(params string[] paths) =>
        ChangeClassifier.Classify(paths, "knowledge-v1", "knowledge-v1", sameRepository: true, actorIsBot: false);

    [Fact]
    public void Docs_only_requires_nothing()
    {
        var result = Classify("docs/Deployment.md", "README.md");

        Assert.False(result.Required);
        Assert.Null(result.Violation);
        Assert.Equal("not required: no AI behaviour or gate policy changed", result.Reason);
    }

    [Fact]
    public void A_behaviour_change_requires_evaluation()
    {
        var result = Classify("ai/prompts/investigation-agent.md");

        Assert.True(result.Required);
        Assert.Null(result.Violation);
        Assert.Equal("required: AI behaviour changed", result.Reason);
    }

    [Fact]
    public void A_policy_only_change_is_still_evaluated()
    {
        var result = Classify("eval/PaymentOps.Evaluation/Program.cs");

        Assert.True(result.Required);
        Assert.Null(result.Violation);
        Assert.Equal("required: gate policy changed", result.Reason);
    }

    [Fact]
    public void Behaviour_and_policy_together_is_a_violation()
    {
        var result = Classify("data/knowledge/src/general/x.md", "eval/datasets/agent-tasks.json", "docs/adr/0013-x.md");

        Assert.Equal("change gate policy and AI behaviour in separate pull requests", result.Violation);
        Assert.Equal(result.Violation, result.Reason);
    }

    [Theory]
    [InlineData("eval/thresholds.json")]
    [InlineData("eval/datasets/retrieval-heldout.json")]
    public void Thresholds_or_datasets_without_an_ADR_is_a_violation(string path)
    {
        Assert.Equal(ChangeClassifier.MissingAdr, Classify(path).Violation);
        Assert.Null(Classify(path, "docs/adr/0013-x.md").Violation);
    }

    [Fact]
    public void A_fork_or_bot_is_untrusted()
    {
        Assert.False(ChangeClassifier.Classify(["ai/manifest.yaml"], "knowledge-v1", "knowledge-v1", sameRepository: false, actorIsBot: false).Trusted);
        Assert.False(ChangeClassifier.Classify(["ai/manifest.yaml"], "knowledge-v1", "knowledge-v1", sameRepository: true, actorIsBot: true).Trusted);
        Assert.True(Classify("ai/manifest.yaml").Trusted);
    }

    [Fact]
    public void An_index_switch_skips_publishing()
    {
        var result = ChangeClassifier.Classify(["ai/manifest.yaml"], "knowledge-v1", "knowledge-v2", sameRepository: true, actorIsBot: false);

        Assert.True(result.IndexSwitch);
        Assert.True(result.Required);
        Assert.Null(result.Violation);
    }

    [Theory]
    [InlineData("data/knowledge/src/general/x.md")]
    [InlineData("tools/PaymentOps.CorpusBuild/Program.cs")]
    public void An_index_switch_with_a_corpus_change_is_a_violation(string path)
    {
        var result = ChangeClassifier.Classify(["ai/manifest.yaml", path], "knowledge-v1", "knowledge-v2", sameRepository: true, actorIsBot: false);

        Assert.Equal(ChangeClassifier.IndexSwitchWithKnowledge, result.Violation);
    }

    [Theory]
    [InlineData("src/PaymentOps.Web/x.cs", false, false)]
    [InlineData("src/PaymentOps.Web/PaymentOps.Web.csproj", true, false)]
    [InlineData("src/PaymentOps.Agent/Investigating/TurnOutcome.cs", true, false)]
    [InlineData("tools/PaymentOps.KnowledgePublish/Program.cs", true, false)]
    [InlineData("tools/PaymentOps.Seed/Program.cs", false, false)]
    [InlineData("Directory.Packages.props", true, false)]
    [InlineData("global.json", true, false)]
    [InlineData("src/global.json", false, false)]
    [InlineData("eval", false, false)]
    [InlineData("evaluation/x.cs", false, false)]
    [InlineData("docs/adr/0012-evaluation-thresholds-from-a-pre-registered-rule.md", false, true)]
    [InlineData("docs/adr/0013-x.md", false, false)]
    [InlineData(".github/workflows/ai-evaluation.yml", false, true)]
    [InlineData(".github/workflows/cd.yml", false, false)]
    public void Glob_matching_is_exact(string path, bool behaviour, bool policy)
    {
        Assert.Equal(behaviour, ChangeClassifier.IsBehaviour(path));
        Assert.Equal(policy, ChangeClassifier.IsGatePolicy(path));
    }

    [Fact]
    public async Task The_command_writes_github_outputs()
    {
        var changed = Temp("ai/prompts/investigation-agent.md\n\n");
        var manifest = Temp("schemaVersion: 1\nretrieval:\n  index: knowledge-v1\n  mode: lexical\n");
        var output = Temp(string.Empty);

        var exit = await ClassifyChangeCommand.RunAsync(["--changed", changed, "--base-manifest", manifest, "--head-manifest", manifest, "--same-repository", "true", "--actor-type", "User", "--github-output", output]);

        Assert.Equal(0, exit);
        Assert.Equal(
            ["required=true", "trusted=true", "violation=", "index-switch=false", "reason=required: AI behaviour changed"],
            File.ReadAllLines(output));
    }

    [Fact]
    public async Task A_manifest_without_an_index_or_a_bad_flag_exits_2()
    {
        var output = Temp(string.Empty);
        var bad = Temp("retrieval: {}\n");

        Assert.Equal(2, await ClassifyChangeCommand.RunAsync(["--changed", Temp("README.md"), "--base-manifest", bad, "--head-manifest", bad, "--same-repository", "true", "--actor-type", "User", "--github-output", output]));
        Assert.Equal(2, await ClassifyChangeCommand.RunAsync(["--changed", Temp("README.md"), "--base-manifest", bad, "--head-manifest", bad, "--same-repository", "maybe", "--actor-type", "User", "--github-output", output]));
    }
}
