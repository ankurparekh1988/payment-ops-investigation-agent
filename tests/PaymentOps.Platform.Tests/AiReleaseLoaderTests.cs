using System.Text;

using PaymentOps.Platform.AiRelease;

namespace PaymentOps.Platform.Tests;

public sealed class AiReleaseLoaderTests
{
    [Fact]
    public void Loads_a_consistent_release()
    {
        var release = AiReleaseLoader.Load(TestRelease.WithOnePrompt());

        Assert.Equal(1, release.SchemaVersion);
        Assert.Equal("chat-blue", release.ActiveChatDeployment);
        Assert.Equal("embed-v1", release.EmbeddingDeployment);
        Assert.Equal("judge", release.JudgeDeployment);

        var prompt = new PromptProvider(release).GetPrompt("grounded-answer");
        Assert.Equal("0.1.0", prompt.Version);
        Assert.Equal(PromptHasher.Hash(TestRelease.Body), prompt.Sha256);
        Assert.Equal(["merchantId"], prompt.Template.Variables);
    }

    [Fact]
    public void An_unknown_prompt_id_is_an_error()
    {
        var provider = new PromptProvider(AiReleaseLoader.Load(TestRelease.WithOnePrompt()));

        Assert.Throws<KeyNotFoundException>(() => provider.GetPrompt("not-in-the-release"));
    }

    [Theory]
    [InlineData("deploymentTypo: GlobalStandard", "deploymentTypo")]
    [InlineData("capacityK: 10\n    capacityK: 20", "Duplicate key")]
    public void Unknown_and_duplicate_keys_fail_instead_of_being_ignored(string replacement, string expected)
    {
        var models = TestRelease.Models.Replace("deploymentType: GlobalStandard\n    capacityK: 10", replacement, StringComparison.Ordinal);
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(models: models)).LoadErrors();

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("capacityK: 10\n  judge|capacityK: 10\n    contentFilterPolicy: strict\n  judge")]
    [InlineData("capacityK: 10\n  embedding|capacityK: 10\n        contentFilterPolicy: default\n  embedding")]
    public void A_content_filter_is_rejected_where_nothing_reads_it(string modelsEdit)
    {
        var parts = modelsEdit.Split('|');
        Assert.Contains(parts[0], TestRelease.Models, StringComparison.Ordinal);
        var models = TestRelease.Models.Replace(parts[0], parts[1], StringComparison.Ordinal);

        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(models: models)).LoadErrors();

        Assert.Contains(errors, e => e.Contains("contentFilterPolicy", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("prompts: {}", "prompts:")]
    [InlineData("active: blue", "active:")]
    [InlineData("deployment: judge", "deployment:")]
    public void An_empty_value_is_reported_instead_of_crashing(string original, string emptied)
    {
        var manifest = TestRelease.Manifest().Replace(original, emptied, StringComparison.Ordinal);

        var errors = new TestRelease().With("manifest.yaml", manifest).LoadErrors();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void An_empty_prompt_hash_is_reported_instead_of_crashing()
    {
        var manifest = TestRelease.Manifest(TestRelease.PromptEntry()).Replace(PromptHasher.Hash(TestRelease.Body), string.Empty, StringComparison.Ordinal);

        var errors = new TestRelease().With("manifest.yaml", manifest).With("prompts/grounded-answer.md", TestRelease.PromptFile()).LoadErrors();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_missing_required_key_fails()
    {
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(prompts: string.Empty)).LoadErrors();

        Assert.Contains(errors, e => e.Contains("prompts", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("schemaVersion: 2", "", "schemaVersion is 2")]
    [InlineData("schemaVersion: 1", "active: blue|active: green", "isn't one of the chat deployments")]
    [InlineData("schemaVersion: 1", "contentFilterPolicy: strict|contentFilterPolicy: relaxed", "models.chat.contentFilterPolicy must be one of")]
    [InlineData("schemaVersion: 1", "\n    contentFilterPolicy: default|", "Missing properties, 'contentFilterPolicy'")]
    [InlineData("schemaVersion: 1", "deployment: judge|deployment: embed-v1", "'embed-v1' is used more than once")]
    [InlineData("schemaVersion: 1", "capacityK: 10\n  embedding|capacityK: 0\n  embedding", "capacityK must be positive")]
    public void Manifest_rules_are_enforced(string schema, string modelsEdit, string expected)
    {
        var models = TestRelease.Models;
        if (modelsEdit.Length > 0)
        {
            var parts = modelsEdit.Split('|');
            Assert.Contains(parts[0], models, StringComparison.Ordinal);
            models = models.Replace(parts[0], parts[1], StringComparison.Ordinal);
        }

        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(models: models, schema: schema)).LoadErrors();

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Grounded_Answer", "0.1.0", "must be lowercase words joined by hyphens")]
    [InlineData("grounded-answer", "v0.1.0", "isn't MAJOR.MINOR.PATCH")]
    [InlineData("grounded-answer", "0.01.0", "isn't MAJOR.MINOR.PATCH")]
    public void Prompt_entries_are_validated(string id, string version, string expected)
    {
        var errors = new TestRelease()
            .With("manifest.yaml", TestRelease.Manifest(TestRelease.PromptEntry(id, version)))
            .With($"prompts/{id}.md", TestRelease.PromptFile(id, version))
            .LoadErrors();

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_prompt_whose_body_changed_without_updating_the_manifest_fails_with_the_expected_hash()
    {
        const string edited = "Answer using only the evidence for {{merchantId}}!\n";
        var errors = TestRelease.WithOnePrompt().With("prompts/grounded-answer.md", TestRelease.PromptFile(body: edited)).LoadErrors();

        Assert.Contains(errors, e => e.Contains(PromptHasher.Hash(edited), StringComparison.Ordinal));
    }

    [Fact]
    public void Prompt_file_and_manifest_must_agree_on_id_and_version()
    {
        var errors = TestRelease.WithOnePrompt()
            .With("prompts/grounded-answer.md", TestRelease.PromptFile(id: "other-prompt", version: "0.2.0"))
            .LoadErrors();

        Assert.Contains(errors, e => e.Contains("declares id 'other-prompt'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("is version 0.2.0; the manifest says 0.1.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Listed_prompts_must_exist_and_present_prompts_must_be_listed()
    {
        var missing = new TestRelease().With("manifest.yaml", TestRelease.Manifest(TestRelease.PromptEntry())).LoadErrors();
        var unlisted = new TestRelease()
            .With("manifest.yaml", TestRelease.Manifest())
            .With("prompts/stray.md", TestRelease.PromptFile("stray"))
            .LoadErrors();

        Assert.Contains(missing, e => e.Contains("listed in the manifest but missing", StringComparison.Ordinal));
        Assert.Contains(unlisted, e => e.Contains("prompts/stray.md isn't listed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("id: grounded-answer\nversion: 0.1.0\n---\nbody", "must start with a '---' line")]
    [InlineData("---\nid: grounded-answer\nversion: 0.1.0\nbody", "no closing '---' line")]
    [InlineData("---\nid: grounded-answer\nversion: 0.1.0\nowner: someone\n---\nbody", "owner")]
    public void Front_matter_is_strict(string file, string expected)
    {
        var errors = TestRelease.WithOnePrompt().With("prompts/grounded-answer.md", file).LoadErrors();

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_byte_order_mark_is_rejected()
    {
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(TestRelease.PromptFile())).ToArray();
        var errors = TestRelease.WithOnePrompt().With("prompts/grounded-answer.md", bytes).LoadErrors();

        Assert.Contains(errors, e => e.Contains("byte-order mark", StringComparison.Ordinal));
    }

    [Fact]
    public void A_prompt_saved_with_windows_line_endings_matches_the_manifest()
    {
        var crlf = TestRelease.PromptFile().Replace("\n", "\r\n", StringComparison.Ordinal);

        var release = AiReleaseLoader.Load(TestRelease.WithOnePrompt().With("prompts/grounded-answer.md", crlf));

        Assert.Equal(PromptHasher.Hash(TestRelease.Body), release.Prompts["grounded-answer"].Sha256);
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var models = TestRelease.Models.Replace("active: blue", "active: green", StringComparison.Ordinal);
        var errors = new TestRelease()
            .With("manifest.yaml", TestRelease.Manifest(TestRelease.PromptEntry(), models, "schemaVersion: 2"))
            .LoadErrors();

        Assert.True(errors.Count >= 3, string.Join(Environment.NewLine, errors));
    }
}
