using System.Text;

using PaymentOps.Platform.Ai;

namespace PaymentOps.Platform.Tests;

public sealed class AiReleaseLoaderTests
{
    [Fact]
    public void Loads_a_consistent_release()
    {
        var release = AiReleaseLoader.Load(TestRelease.WithOnePrompt());

        Assert.Equal("chat-blue", release.ActiveChatDeployment);
        Assert.Equal("embed-v1", release.EmbeddingDeployment);
        Assert.Equal("judge", release.JudgeDeployment);
        Assert.Equal(["chat-blue", "embed-v1", "judge"], release.Deployments.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new DeploymentIdentity("gpt-test", "2026-01-01", "GlobalStandard", "strict"), release.Deployments["chat-blue"]);
        Assert.Null(release.Deployments["embed-v1"].ContentFilterPolicy);
        Assert.Equal("default", release.Deployments["judge"].ContentFilterPolicy);

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
        var models = TestRelease.EditModels("deploymentType: GlobalStandard\n    capacityK: 10", replacement);
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(models: models)).LoadErrors();

        Assert.Contains(errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("capacityK: 10\n  judge", "capacityK: 10\n    contentFilterPolicy: strict\n  judge")]
    [InlineData("active: blue", "active: blue\n    contentFilterPolicy: strict")]
    public void A_content_filter_is_rejected_where_nothing_reads_it(string from, string to)
    {
        var models = TestRelease.EditModels(from, to);

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

    [Fact]
    public void An_unsupported_schema_version_fails()
    {
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(schema: "schemaVersion: 2")).LoadErrors();

        Assert.Contains(errors, e => e.Contains("schemaVersion is 2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("active: blue", "active: green", "isn't one of the chat deployments")]
    [InlineData("contentFilterPolicy: strict", "contentFilterPolicy: relaxed", "models.chat.deployments.blue.contentFilterPolicy must be one of")]
    [InlineData("\n        contentFilterPolicy: strict", "", "Missing properties, 'contentFilterPolicy'")]
    [InlineData("\n    contentFilterPolicy: default", "", "Missing properties, 'contentFilterPolicy'")]
    [InlineData("deployment: judge", "deployment: embed-v1", "'embed-v1' is used more than once")]
    [InlineData("capacityK: 10\n  judge", "capacityK: 0\n  judge", "capacityK must be positive")]
    public void Manifest_rules_are_enforced(string from, string to, string expected)
    {
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(models: TestRelease.EditModels(from, to))).LoadErrors();

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
        var models = TestRelease.EditModels("active: blue", "active: green");
        var errors = new TestRelease()
            .With("manifest.yaml", TestRelease.Manifest(TestRelease.PromptEntry(), models, "schemaVersion: 2"))
            .LoadErrors();

        Assert.True(errors.Count >= 3, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void Loads_the_knowledge_index_name()
    {
        Assert.Equal("knowledge-v1", AiReleaseLoader.Load(TestRelease.WithOnePrompt()).KnowledgeIndex);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Knowledge-v1")]
    [InlineData("-knowledge")]
    [InlineData("knowledge-")]
    [InlineData("knowledge--v1")]
    [InlineData("knowledge_v1")]
    public void An_invalid_index_name_fails(string name)
    {
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(retrieval: $"retrieval:\n  index: {name}")).LoadErrors();

        Assert.Contains(errors, e => e.Contains("retrieval.index", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_retrieval_section_fails()
    {
        var errors = new TestRelease().With("manifest.yaml", TestRelease.Manifest(retrieval: string.Empty)).LoadErrors();

        Assert.Contains(errors, e => e.Contains("retrieval", StringComparison.OrdinalIgnoreCase));
    }
}
