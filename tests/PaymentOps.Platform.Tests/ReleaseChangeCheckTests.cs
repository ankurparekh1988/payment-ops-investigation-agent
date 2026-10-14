using System.Text;

using PaymentOps.Platform.Ai;

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
        Assert.Empty(ReleaseChangeCheck.Compare(BaseWithPrompt(baseVersion, Before), HeadWithPrompt(headVersion, headBody)));
    }

    [Theory]
    [InlineData("0.2.0", After, "0.2.0", "must increase")]
    [InlineData("0.2.0", After, "0.1.9", "must increase")]
    [InlineData("0.2.0", Before, "0.2.1", "is unchanged but its version went")]
    public void Rejects_versions_that_do_not_track_the_body(string baseVersion, string headBody, string headVersion, string expected)
    {
        var errors = ReleaseChangeCheck.Compare(BaseWithPrompt(baseVersion, Before), HeadWithPrompt(headVersion, headBody));

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void New_and_removed_prompts_are_allowed()
    {
        Assert.Empty(ReleaseChangeCheck.Compare(Base(TestRelease.Manifest()), HeadWithPrompt("0.1.0", Before)));
        Assert.Empty(ReleaseChangeCheck.Compare(BaseWithPrompt("0.1.0", Before), Head(TestRelease.Models)));
    }

    [Fact]
    public void A_base_with_another_schema_is_compared_on_the_fields_it_shares()
    {
        // The release before this schema had no prompts section and no content filters in the manifest.
        var legacyModels = TestRelease.EditModels("\n        contentFilterPolicy: strict", string.Empty)
            .Replace("\n    contentFilterPolicy: default", string.Empty, StringComparison.Ordinal);
        var legacyBase = Base($"manifestVersion: 0.1.0\n{legacyModels}\nunknownSection: {{}}\n");

        Assert.Empty(ReleaseChangeCheck.Compare(legacyBase, HeadWithPrompt("0.1.0", Before)));
    }

    [Theory]
    [InlineData("active: blue", "active: green", "blue:\n        deployment: chat-blue", "green:\n        deployment: chat-green", "chat-blue")]
    [InlineData("deployment: embed-v1", "deployment: embed-v2", null, null, "embed-v1")]
    [InlineData("deployment: judge", "deployment: judge-v2", null, null, "judge")]
    public void A_deployment_the_base_release_uses_cannot_be_removed(string from, string to, string? from2, string? to2, string removed)
    {
        // The infrastructure is applied before the new build is deployed, so the running app would call it.
        var models = TestRelease.EditModels(from, to);
        if (from2 is not null)
        {
            models = models.Replace(from2, to2, StringComparison.Ordinal);
        }

        var errors = ReleaseChangeCheck.Compare(Base(TestRelease.Manifest()), Head(models));

        Assert.Contains(errors, e => e.Contains($"'{removed}' is in use on the base branch and removed here", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("model: gpt-test\n        version", "model: gpt-next\n        version", "chat-blue", "model")]
    [InlineData("version: \"2026-01-01\"\n        deploymentType", "version: \"2026-02-01\"\n        deploymentType", "chat-blue", "version")]
    [InlineData("deploymentType: GlobalStandard\n        capacityK", "deploymentType: DataZoneStandard\n        capacityK", "chat-blue", "deploymentType")]
    [InlineData("contentFilterPolicy: strict", "contentFilterPolicy: default", "chat-blue", "contentFilterPolicy")]
    [InlineData("model: embed-test", "model: embed-next", "embed-v1", "model")]
    [InlineData("contentFilterPolicy: default", "contentFilterPolicy: strict", "judge", "contentFilterPolicy")]
    public void A_deployment_the_base_release_uses_cannot_change_what_it_runs(string from, string to, string deployment, string field)
    {
        // Terraform would change it under the running app, which was built for the old one.
        var errors = ReleaseChangeCheck.Compare(Base(TestRelease.Manifest()), Head(TestRelease.EditModels(from, to)));

        Assert.Contains(errors, e => e.Contains($"'{deployment}' is in use on the base branch and its {field} changes", StringComparison.Ordinal));
    }

    [Fact]
    public void Capacity_of_a_deployment_in_use_can_change()
    {
        var models = TestRelease.EditModels("capacityK: 10\n        contentFilterPolicy", "capacityK: 99\n        contentFilterPolicy");

        Assert.Empty(ReleaseChangeCheck.Compare(Base(TestRelease.Manifest()), Head(models)));
    }

    [Fact]
    public void Switching_active_while_keeping_the_old_slot_is_allowed()
    {
        var switched = BothSlots().Replace("active: blue", "active: green", StringComparison.Ordinal);

        Assert.Empty(ReleaseChangeCheck.Compare(Base(TestRelease.Manifest(models: BothSlots())), Head(switched)));
    }

    [Fact]
    public void An_inactive_slot_can_change_in_place()
    {
        // Nothing calls the candidate slot except evaluation, so it can be revised before promotion.
        var revised = BothSlots().Replace("version: \"2026-02-01\"", "version: \"2026-03-01\"", StringComparison.Ordinal);

        Assert.Empty(ReleaseChangeCheck.Compare(Base(TestRelease.Manifest(models: BothSlots())), Head(revised)));
    }

    private static string BothSlots() => TestRelease.EditModels(
        "      blue:\n",
        "      green:\n        deployment: chat-green\n        model: gpt-test\n        version: \"2026-02-01\"\n        deploymentType: GlobalStandard\n        capacityK: 10\n        contentFilterPolicy: strict\n      blue:\n");

    [Fact]
    public void A_malformed_base_is_reported_as_the_base()
    {
        var errors = ReleaseChangeCheck.Compare(Base("models: [unclosed\n"), Head(TestRelease.Models));

        Assert.Contains(errors, e => e.StartsWith("The base branch's manifest isn't valid YAML", StringComparison.Ordinal));
    }

    private static byte[] Base(string manifest) => Encoding.UTF8.GetBytes(manifest);

    private static byte[] BaseWithPrompt(string version, string body) =>
        Base(TestRelease.Manifest(TestRelease.PromptEntry(version: version, body: body)));

    private static AiRelease Head(string models) =>
        AiReleaseLoader.Load(new TestRelease().With("manifest.yaml", TestRelease.Manifest(models: models)));

    private static AiRelease HeadWithPrompt(string version, string body) =>
        AiReleaseLoader.Load(new TestRelease()
            .With("manifest.yaml", TestRelease.Manifest(TestRelease.PromptEntry(version: version, body: body)))
            .With("prompts/grounded-answer.md", TestRelease.PromptFile(version: version, body: body)));
}
