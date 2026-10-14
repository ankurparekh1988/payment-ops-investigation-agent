using System.Text;

using PaymentOps.Platform.AiRelease;

namespace PaymentOps.Platform.Tests;

public sealed class ReleaseChangeCheckTests
{
    private const string Before = "Original wording.\n";
    private const string After = "Revised wording.\n";

    [Theory]
    [InlineData("0.2.0", After, "0.2.1")]
    [InlineData("0.2.0", After, "0.3.0")]
    [InlineData("0.2.0", After, "1.0.0")]
    [InlineData("0.2.0", Before, "0.2.0")]
    public void Accepts_a_higher_version_for_a_changed_body_and_the_same_version_for_an_unchanged_one(
        string baseVersion, string headBody, string headVersion)
    {
        Assert.Empty(Compare(Entry(baseVersion, Before), Entry(headVersion, headBody)));
    }

    [Theory]
    [InlineData("0.2.0", After, "0.2.0", "must increase")]
    [InlineData("0.2.0", After, "0.1.9", "must increase")]
    [InlineData("0.2.0", Before, "0.2.1", "is unchanged but its version went")]
    public void Rejects_versions_that_do_not_track_the_body(string baseVersion, string headBody, string headVersion, string expected)
    {
        var errors = Compare(Entry(baseVersion, Before), Entry(headVersion, headBody));

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void New_and_removed_prompts_are_allowed()
    {
        Assert.Empty(Compare("prompts: {}", Entry("0.1.0", Before)));
        Assert.Empty(Compare(Entry("0.1.0", Before), "prompts: {}"));
    }

    [Fact]
    public void Either_side_may_use_a_different_schema()
    {
        var legacyBase = Bytes($"manifestVersion: 0.1.0\n{TestRelease.Models}\n");
        var futureHead = Bytes(TestRelease.Manifest(Entry("0.1.0", Before)) + "retrieval:\n  topK: 5\n");

        Assert.Empty(ReleaseChangeCheck.Compare(legacyBase, Manifest(Entry("0.1.0", Before))));
        Assert.Empty(ReleaseChangeCheck.Compare(Manifest(Entry("0.1.0", Before)), futureHead));
    }

    [Fact]
    public void The_base_branchs_active_chat_deployment_cannot_be_removed_in_the_same_change()
    {
        // Promote green and drop blue together: the running app would call chat-blue until it's redeployed.
        var promoted = TestRelease.Models
            .Replace("active: blue", "active: green", StringComparison.Ordinal)
            .Replace("blue:\n        deployment: chat-blue", "green:\n        deployment: chat-green", StringComparison.Ordinal);

        var errors = ReleaseChangeCheck.Compare(Manifest("prompts: {}"), Bytes(TestRelease.Manifest(models: promoted)));

        Assert.Contains(errors, e => e.Contains("'chat-blue' is active on the base branch and removed here", StringComparison.Ordinal));
    }

    [Fact]
    public void Switching_active_while_keeping_the_old_slot_is_allowed()
    {
        var bothSlots = TestRelease.Models.Replace(
            "      blue:\n",
            "      green:\n        deployment: chat-green\n        model: gpt-test\n        version: \"2026-02-01\"\n        deploymentType: GlobalStandard\n        capacityK: 10\n      blue:\n",
            StringComparison.Ordinal);
        var switched = bothSlots.Replace("active: blue", "active: green", StringComparison.Ordinal);

        Assert.Empty(ReleaseChangeCheck.Compare(Bytes(TestRelease.Manifest(models: bothSlots)), Bytes(TestRelease.Manifest(models: switched))));
    }

    [Fact]
    public void A_malformed_base_is_reported_as_the_base()
    {
        var errors = ReleaseChangeCheck.Compare(Bytes("models: [unclosed\n"), Manifest("prompts: {}"));

        Assert.Contains(errors, e => e.StartsWith("The base branch's manifest isn't valid YAML", StringComparison.Ordinal));
    }

    private static string Entry(string version, string body) => TestRelease.PromptEntry(version: version, body: body);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Manifest(string prompts) => Bytes(TestRelease.Manifest(prompts));

    private static IReadOnlyList<string> Compare(string basePrompts, string headPrompts) =>
        ReleaseChangeCheck.Compare(Manifest(basePrompts), Manifest(headPrompts));
}
