using System.Text;

using PaymentOps.Platform.AiRelease;

namespace PaymentOps.Platform.Tests;

public sealed class PromptVersionCheckTests
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
    public void A_base_from_before_prompt_versioning_has_nothing_to_compare()
    {
        var legacy = Encoding.UTF8.GetBytes("manifestVersion: 0.1.0\nmodels: {}\n");

        Assert.Empty(PromptVersionCheck.Compare(legacy, Manifest(Entry("0.1.0", Before))));
    }

    private static string Entry(string version, string body) => TestRelease.PromptEntry(version: version, body: body);

    private static byte[] Manifest(string prompts) => Encoding.UTF8.GetBytes(TestRelease.Manifest(prompts));

    private static IReadOnlyList<string> Compare(string basePrompts, string headPrompts) =>
        PromptVersionCheck.Compare(Manifest(basePrompts), Manifest(headPrompts));
}
