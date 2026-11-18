using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Telemetry;
using PaymentOps.Agent.Tests;
using PaymentOps.Evaluation.Commands;
using PaymentOps.Evaluation.Pricing;
using PaymentOps.Evaluation.Reporting;
using PaymentOps.Evaluation.Running;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Platform.Ai;
using PaymentOps.Tests.Shared;

using static PaymentOps.Evaluation.Tests.EvaluationFakes;

namespace PaymentOps.Evaluation.Tests;

/// <summary>
/// What a report vouches for: the source tree and inputs it ran, an index that read whole and didn't change, and an
/// all-in cost from the app's own usage, priced part by part, or a reason it couldn't be (ADR 0023).
/// </summary>
public sealed class ReportIdentityTests
{
    private const string Chat = "chat-blue";
    private const string Small = "chat-small";
    private const string Screen = "screen";

    private static readonly IReadOnlyDictionary<string, DeploymentIdentity> Deployments = new Dictionary<string, DeploymentIdentity>
    {
        [Chat] = new("gpt-5.4-mini", "2026-03-17", "GlobalStandard", "strict"),
        [Small] = new("gpt-5.4-nano", "2026-03-17", "GlobalStandard", "strict"),
        [Screen] = new("gpt-5.4-mini", "2026-03-17", "GlobalStandard", "strict"),
    };

    private static PriceList Prices(string currency = "USD") => new(
        [
            new Price("gpt-5.4-mini", "2026-03-17", "GlobalStandard", "USD", 0.75m, 0.075m, 4.50m, "2026-03-01", "2026-11-18", "https://prices.azure.com/"),
            new Price("gpt-5.4-nano", "2026-03-17", "GlobalStandard", currency, 0.20m, 0.02m, 1.25m, "2026-03-01", "2026-11-18", "https://prices.azure.com/"),
        ],
        [
            new ContentSafetyPrice("prompt_shields", "USD", 0.375m, "2025-09-01", "2026-11-18", "https://prices.azure.com/"),
            new ContentSafetyPrice("text_analysis", "USD", 0.375m, "2025-09-01", "2026-11-18", "https://prices.azure.com/"),
        ]);

    private static CaseUsage Usage(Dictionary<string, DeploymentTokens>? deployments = null, Dictionary<string, int>? records = null, string[]? incomplete = null) =>
        new(deployments ?? new Dictionary<string, DeploymentTokens> { [Chat] = new(2, 2, 1_000_000, 0, 100_000) }, records ?? [], incomplete ?? [], 0);

    private static CaseResult Assessed(CaseUsage? usage) => new("c1", "assessed") { Usage = usage };

    private static KnowledgeSearchDocument Doc(string id = "runbook-a--01", string classification = "general") =>
        new(id, "runbook-a", "Missing batch", "runbook", classification, "Steps", "Check", "Steps > Check", 1, "Check the file.");

    [Fact]
    public async Task Chat_tokens_are_counted_once()
    {
        var recorder = new CaseRecorder(Screen);
        var answer = ScriptedChatClient.Text("ok");
        answer.Usage = new UsageDetails { InputTokenCount = 9_999, OutputTokenCount = 9_999 };
        await recorder.Observe(new ScriptedChatClient(_ => answer)).GetResponseAsync("q");
        using var usage = TurnUsage.Begin();
        TurnUsage.RecordTokens(Chat, 500, 50, 100);
        TurnUsage.RecordTokens(Screen, 70, 7);

        recorder.Completed(Guid.NewGuid(), usage.Snapshot());
        var result = recorder.ToResult("c1", new Agent.Contracts.AssessmentResult.NotAssessed(Agent.Contracts.NotAssessedReason.AssessmentUnverified), ProposalStates.None, 1, null);

        Assert.Equal((500, 100, 50), (result.InputTokens, result.CachedInputTokens, result.OutputTokens));
        Assert.Equal(new DeploymentTokens(0, 0, 70, 0, 7), result.Usage!.Deployments[Screen]);
    }

    [Fact]
    public void Cached_input_is_priced_at_its_own_rate()
    {
        var usage = Usage(new Dictionary<string, DeploymentTokens> { [Small] = new(1, 1, 1_000_000, 400_000, 0) });

        var cost = AllInCost.Of([Assessed(usage)], Deployments, Prices());

        Assert.Equal((600_000 * 0.20m + 400_000 * 0.02m) / 1_000_000m, cost.Total);
    }

    [Fact]
    public void Classifier_calls_are_priced_at_the_screen_deployment()
    {
        var usage = Usage(new Dictionary<string, DeploymentTokens> { [Screen] = new(1, 1, 1_000_000, 0, 0) });

        Assert.Equal(0.75m, AllInCost.Of([Assessed(usage)], Deployments, Prices()).Total);
    }

    [Fact]
    public void Content_safety_records_are_priced_per_thousand_per_service()
    {
        var usage = Usage(new Dictionary<string, DeploymentTokens>(), new Dictionary<string, int> { ["prompt_shields"] = 2_000, ["text_analysis"] = 1_000 });

        var cost = AllInCost.Of([Assessed(usage), new CaseResult("c2", "incomplete") { Usage = Usage(new Dictionary<string, DeploymentTokens>()) }], Deployments, Prices());

        Assert.Equal((AllInCost.Priced, 3 * 0.375m, 3 * 0.375m / 2), (cost.Status, cost.Total, cost.PerValidatedInvestigation));
    }

    [Theory]
    [InlineData("usage_missing")]
    [InlineData("usage_incomplete")]
    [InlineData("unpriced:chat-x")]
    [InlineData("unpriced:content_safety")]
    [InlineData("currency_mismatch")]
    [InlineData("no_validated_results")]
    public void Unknown_usage_unpriced_component_mixed_currency_or_no_validated_results_is_inconclusive(string status)
    {
        var (results, prices) = status switch
        {
            "usage_missing" => ((CaseResult[])[Assessed(null)], Prices()),
            "usage_incomplete" => ([Assessed(Usage(incomplete: ["model outcome unknown"]))], Prices()),
            "unpriced:chat-x" => ([Assessed(Usage(new Dictionary<string, DeploymentTokens> { ["chat-x"] = new(1, 1, 10, 0, 1) }))], Prices()),
            "unpriced:content_safety" => ([Assessed(Usage(records: new Dictionary<string, int> { ["content_safety"] = 1 }))], Prices()),
            "currency_mismatch" => ([Assessed(Usage(new Dictionary<string, DeploymentTokens> { [Chat] = new(1, 1, 10, 0, 1), [Small] = new(1, 1, 10, 0, 1) }))], Prices("EUR")),
            _ => ([new CaseResult("c1", "not-assessed:assessment-unverified") { Usage = Usage() }], Prices()),
        };

        var cost = AllInCost.Of(results, Deployments, prices);

        Assert.Equal((status, (decimal?)null, (decimal?)null), (cost.Status, cost.Total, cost.PerValidatedInvestigation));
    }

    [Fact]
    public void Report_records_tree_commit_and_dirty()
    {
        var source = ReportSource.Current();

        Assert.Matches("^[0-9a-f]{40}$", source.Commit);
        Assert.Matches("^[0-9a-f]{40}$", source.Tree!);
        Assert.Equal(RepositoryCommit.Git("status --porcelain --untracked-files=no") is not "", source.Dirty);
    }

    public static TheoryData<string> Fields() => ["Id", "DocId", "Title", "DocType", "Classification", "H2", "H3", "SectionPath", "Ordinal", "Content"];

    [Theory]
    [MemberData(nameof(Fields))]
    public void Index_fingerprint_changes_when_any_field_changes(string field)
    {
        var doc = Doc();
        var changed = field switch
        {
            "Id" => doc with { Id = "runbook-a--02" },
            "DocId" => doc with { DocId = "runbook-b" },
            "Title" => doc with { Title = "Other" },
            "DocType" => doc with { DocType = "policy" },
            "Classification" => doc with { Classification = "restricted" },
            "H2" => doc with { H2 = "Other" },
            "H3" => doc with { H3 = "Other" },
            "SectionPath" => doc with { SectionPath = "Other" },
            "Ordinal" => doc with { Ordinal = 2 },
            _ => doc with { Content = "Other." },
        };

        Assert.NotEqual(IndexFingerprint.Hash([doc]), IndexFingerprint.Hash([changed]));
        Assert.Equal(10, typeof(KnowledgeSearchDocument).GetProperties().Length);
    }

    [Fact]
    public void Index_fingerprint_ignores_read_order() =>
        Assert.Equal(IndexFingerprint.Hash([Doc("a--1"), Doc("b--1")]), IndexFingerprint.Hash([Doc("b--1"), Doc("a--1")]));

    [Theory]
    [InlineData("a--1", "a--1", "inconsistent: a key read twice")]
    [InlineData("b--1", "a--1", "inconsistent: keys out of order")]
    public void Duplicate_or_out_of_order_keys_make_the_snapshot_invalid(string first, string second, string problem)
    {
        var index = IndexFingerprint.Of("knowledge-eval-v1", [Doc(first), Doc(second)]);

        Assert.Equal((null, problem), (index.Fingerprint, index.Problem));
    }

    private sealed class FakeIndex(params Func<IReadOnlyList<KnowledgeSearchDocument>>[] reads) : IKnowledgeIndexClient
    {
        private int _read;

        public Task<IReadOnlyList<KnowledgeSearchDocument>> ReadAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(reads[Math.Min(_read++, reads.Length - 1)]());

        public Task EnsureIndexAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteIndexAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> FindExistingAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UploadAsync(IReadOnlyCollection<KnowledgeSearchDocument> documents, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task An_empty_or_unreadable_index_makes_the_run_invalid()
    {
        var empty = await IndexFingerprint.ReadAsync("i", new FakeIndex(() => []), CancellationToken.None);
        var unreadable = await IndexFingerprint.ReadAsync("i", new FakeIndex(() => throw new InvalidOperationException("search down")), CancellationToken.None);

        Assert.Equal("empty", empty.Problem);
        Assert.Equal("unreadable (InvalidOperationException)", unreadable.Problem);
        Assert.False(empty.Ended(empty).Stable);
    }

    private static async Task<JsonNode> InvestigateAsync(IKnowledgeIndexClient index, params string[] extra)
    {
        var output = Path.GetTempFileName();
        var args = CommandTests.Args(CommandTests.Dataset(), extra);
        args[Array.IndexOf(args, "--out") + 1] = output;
        var outcome = new RunOutcome([new CaseResult("c1", "assessed")], 0);

        Assert.Equal(0, await InvestigationEvalCommand.RunAsync(args, (_, _) => new ScriptedRunner(outcome, outcome, outcome), _ => null, TimeSpan.Zero, _ => index));
        return JsonNode.Parse(File.ReadAllText(output))!;
    }

    [Fact]
    public async Task An_index_that_reads_the_same_at_both_ends_is_stable()
    {
        var report = await InvestigateAsync(new FakeIndex(() => [Doc()]));

        var index = report["inputs"]!["index"]!;
        Assert.True(index["stable"]!.GetValue<bool>());
        Assert.Equal(IndexFingerprint.Hash([Doc()]), index["fingerprint"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_index_that_changes_during_the_run_makes_it_invalid()
    {
        var report = await InvestigateAsync(new FakeIndex(() => [Doc()], () => [Doc(classification: "restricted")]));

        var index = report["inputs"]!["index"]!;
        Assert.False(index["stable"]!.GetValue<bool>());
        Assert.Equal("changed during the run", index["problem"]!.GetValue<string>());
    }

    [Fact]
    public async Task Routing_option_overrides_the_manifest_for_the_run_only()
    {
        var releases = new List<AiRelease>();
        var output = Path.GetTempFileName();
        var args = CommandTests.Args(CommandTests.Dataset(), "--routing", "cascade");
        args[Array.IndexOf(args, "--out") + 1] = output;
        var outcome = new RunOutcome([new CaseResult("c1", "assessed")], 0);

        await InvestigationEvalCommand.RunAsync(args, (release, _) =>
        {
            releases.Add(release);
            return new ScriptedRunner(outcome, outcome, outcome);
        }, _ => null, TimeSpan.Zero);

        Assert.Equal(RoutingMode.Cascade, Assert.Single(releases).RoutingMode);
        Assert.Equal("cascade", JsonNode.Parse(File.ReadAllText(output))!["inputs"]!["routing"]!.GetValue<string>());
        Assert.Equal(RoutingMode.Off, AiReleaseLoader.Load(new DirectoryAiReleaseSource(Path.Combine(RepositoryRoot.FullPath, "ai"))).RoutingMode);
    }

    [Fact]
    public async Task An_unknown_routing_option_exits_2()
    {
        var outcome = new RunOutcome([new CaseResult("c1", "assessed")], 0);

        Assert.Equal(2, await InvestigationEvalCommand.RunAsync(CommandTests.Args(CommandTests.Dataset(), "--routing", "nano"), (_, _) => new ScriptedRunner(outcome), _ => null, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schema_3_round_trips()
    {
        var report = await InvestigateAsync(new FakeIndex(() => [Doc()]));
        var path = Path.GetTempFileName();
        File.WriteAllText(path, report.ToJsonString());

        var loaded = EvaluationReport.Load(path);

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(ReportSource.Current().Tree, loaded.Source!.Tree);
        Assert.Equal("off", loaded.Inputs!.Routing);
        Assert.Equal(AllInCost.UsageMissing, loaded.Cost.AllInStatus);
        Assert.StartsWith("sha256:", Assert.Single(loaded.Inputs.Datasets).Value, StringComparison.Ordinal);
    }
}
