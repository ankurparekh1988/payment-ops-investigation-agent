using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;
using PaymentOps.Agent.Safety;
using PaymentOps.Agent.Telemetry;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;
using PaymentOps.Tools.Knowledge;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>
/// A read the small model made is served to chat from the investigation's cache, but only the raw output: it's screened
/// again and registered in chat's own evidence, so chat cites only ids issued to it (ADR 0023).
/// </summary>
public sealed class ReadReuseTests
{
    private const string Submit = ToolNames.SubmitAssessment;
    private static readonly RetrievedSection SectionA = new("ka", "runbook-a", "Missing batch", "Missing batch > Steps", "Check the file.", "general", null);
    private static readonly RetrievedSection SectionB = new("kb", "runbook-b", "Late payout", "Late payout > Steps", "Check the processor.", "general", null);

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Respond(ChatResponse response) => _ => response;

    private static ChatResponse Search(string query) => Call(ToolNames.KnowledgeSearch, new { query });

    private static ChatResponse Profile(string merchant = "M-1042") => Call(ToolNames.MerchantProfile, new { merchant_id = merchant });

    /// <summary>Answers a query with the sections whose key it names, counting every search that reached it.</summary>
    private sealed class CountingKnowledge(params RetrievedSection[] sections) : IKnowledgeLookup
    {
        public int Searches;

        public Func<int, bool> Unavailable { get; init; } = _ => false;

        public Task<IReadOnlyList<RetrievedSection>> RetrieveAsync(string question, KnowledgeAccess access, CancellationToken cancellationToken)
        {
            var search = Interlocked.Increment(ref Searches);
            return Unavailable(search)
                ? throw new KnowledgeUnavailableException("down")
                : Task.FromResult<IReadOnlyList<RetrievedSection>>([.. sections.Where(s => question.Contains(s.Key, StringComparison.Ordinal))]);
        }

        public Task<RetrievedSection?> FindAsync(string sectionKey, KnowledgeAccess access, CancellationToken cancellationToken) =>
            Task.FromResult<RetrievedSection?>(null);
    }

    private sealed record Run(AssessmentResult Result, ScriptedChatClient Chat, TurnUsage Usage, List<InvestigationProgress> Progress);

    // The small model makes these calls and then answers in text twice: no submission, so chat gets one attempt.
    private static async Task<Run> InvestigateAsync(
        IKnowledgeLookup knowledge, ChatResponse[] smallCalls, Func<IReadOnlyList<ChatMessage>, ChatResponse>[] chatScript, IUntrustedTextScreen? screen = null, string question = RunnerExtensions.Question)
    {
        using var fixture = new AgentFixture(knowledge: knowledge);
        using var usage = TurnUsage.Begin();
        var small = new ScriptedChatClient([.. smallCalls.Select(Respond), Respond(Text("Late.")), Respond(Text("Still late."))]);
        var chat = new ScriptedChatClient(chatScript);
        var progress = new ProgressTests.RecordingProgress();
        var result = await fixture.Runner(new ModelRouting(RoutingMode.Cascade, chat, small), screen: screen)
            .InvestigateAsync(question, [], progress, Guid.NewGuid(), CancellationToken.None);
        return new Run(result, chat, usage, progress.Events);
    }

    private static int Reused(TurnUsage usage) => usage.CacheHits.GetValueOrDefault(ReadCache.Name);

    [Fact]
    public async Task A_successful_read_is_served_to_the_second_attempt_without_calling_the_source()
    {
        var knowledge = new CountingKnowledge(SectionA);

        var run = await InvestigateAsync(
            knowledge,
            [Profile(), Search("ka")],
            [Respond(Profile()), Respond(Search("ka")), Respond(Call(Submit, Submission(claimCitations: ["S1"])))]);

        Assert.IsType<AssessmentResult.Assessed>(run.Result);
        Assert.Equal(1, knowledge.Searches);
        Assert.Equal(2, Reused(run.Usage));
    }

    // Dates must be exactly YYYY-MM-DD: a padded date the tool rejects must not be answered from the cache.
    [Fact]
    public async Task A_whitespace_variant_of_a_cached_read_is_validated_not_served()
    {
        static ChatResponse Batches(string start) =>
            Call(ToolNames.SettlementBatches, new { merchant_id = "M-1042", start_date = start, end_date = "2026-11-10" });

        var run = await InvestigateAsync(
            new CountingKnowledge(),
            [Batches("2026-11-09")],
            [Respond(Batches(" 2026-11-09")), Respond(Call(Submit, Submission())), Respond(Call(Submit, Submission()))]);

        Assert.Equal(0, Reused(run.Usage));
        Assert.Contains("invalid_argument", ToolResults(run.Chat.Requests[1].Messages)[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_read_is_not_reused()
    {
        var knowledge = new CountingKnowledge(SectionA) { Unavailable = search => search == 1 };

        var run = await InvestigateAsync(knowledge, [Search("ka")], [Respond(Search("ka")), Respond(Call(Submit, Submission()))]);

        Assert.Equal(2, knowledge.Searches);
        Assert.Equal(0, Reused(run.Usage));
    }

    [Theory]
    [InlineData("M-2087")]
    [InlineData("M-9999")]
    public async Task Refused_unnamed_or_not_found_reads_are_not_reused(string merchant)
    {
        var question = merchant == "M-9999" ? RunnerExtensions.Naming(["M-1042", "M-9999"]) : RunnerExtensions.Question;

        var run = await InvestigateAsync(
            new CountingKnowledge(), [Profile(merchant)], [Respond(Profile(merchant)), Respond(Call(Submit, Submission())), Respond(Call(Submit, Submission()))], question: question);

        Assert.Equal(0, Reused(run.Usage));
        Assert.DoesNotContain("\"status\":\"ok\"", ToolResults(run.Chat.Requests[1].Messages)[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_is_reused_only_within_its_investigation()
    {
        var knowledge = new CountingKnowledge(SectionA);
        using var fixture = new AgentFixture(knowledge: knowledge);

        await fixture.Runner(new ScriptedChatClient(Respond(Search("ka")), Respond(Call(Submit, Submission())), Respond(Call(Submit, Submission())))).InvestigateAsync(RunnerExtensions.Question, CancellationToken.None);
        await fixture.Runner(new ScriptedChatClient(Respond(Search("ka")), Respond(Call(Submit, Submission())), Respond(Call(Submit, Submission())))).InvestigateAsync(RunnerExtensions.Question, CancellationToken.None);

        Assert.Equal(2, knowledge.Searches);
    }

    [Fact]
    public async Task Cached_raw_output_is_never_treated_as_screened()
    {
        var screen = ScriptedScreen.AllClean;

        await InvestigateAsync(new CountingKnowledge(SectionA), [Search("ka")], [Respond(Search("ka")), Respond(Call(Submit, Submission()))], screen);

        Assert.Equal(2, screen.Screened.Count(text => text.Contains("Check the file.", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(ScreenVerdict.Attack)]
    [InlineData(ScreenVerdict.Harmful)]
    public async Task A_value_withheld_in_attempt_one_is_withheld_again(ScreenVerdict verdict)
    {
        var screen = new ScriptedScreen(text => text.Contains("Check the file.", StringComparison.Ordinal) ? verdict : ScreenVerdict.Clean);

        var run = await InvestigateAsync(
            new CountingKnowledge(SectionA), [Profile(), Search("ka")], [Respond(Profile()), Respond(Search("ka")), Respond(Call(Submit, Submission()))], screen);

        Assert.DoesNotContain("Check the file.", ToolResults(run.Chat.Requests[2].Messages)[^1], StringComparison.Ordinal);
        Assert.Contains(Assert.IsType<AssessmentResult.Assessed>(run.Result).Gaps, gap => gap.Contains("withheld", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unavailable_verdict_is_screened_afresh_and_withheld_if_still_unavailable()
    {
        var screen = new ScriptedScreen(text => text.Contains("Check the file.", StringComparison.Ordinal) ? ScreenVerdict.Unavailable : ScreenVerdict.Clean);

        var run = await InvestigateAsync(new CountingKnowledge(SectionA), [Search("ka")], [Respond(Search("ka")), Respond(Call(Submit, Submission()))], screen);

        Assert.Equal(2, screen.Screened.Count(text => text.Contains("Check the file.", StringComparison.Ordinal)));
        Assert.DoesNotContain("Check the file.", ToolResults(run.Chat.Requests[1].Messages)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_unavailable_in_attempt_one_and_clean_now_is_shown()
    {
        var calls = 0;
        var screen = new ScriptedScreen(text =>
            text.Contains("Check the file.", StringComparison.Ordinal) && Interlocked.Increment(ref calls) == 1 ? ScreenVerdict.Unavailable : ScreenVerdict.Clean);

        var run = await InvestigateAsync(new CountingKnowledge(SectionA), [Search("ka")], [Respond(Search("ka")), Respond(Call(Submit, Submission()))], screen);

        Assert.Contains("Check the file.", ToolResults(run.Chat.Requests[1].Messages)[^1], StringComparison.Ordinal);
    }

    // Chat searches B first, so the reused A becomes its S2: ids are chat's own, and S2 names A.
    [Fact]
    public async Task Knowledge_sections_get_attempt_twos_own_citation_ids()
    {
        var run = await InvestigateAsync(
            new CountingKnowledge(SectionA, SectionB),
            [Search("ka")],
            [Respond(Profile()), Respond(Search("kb")), Respond(Search("ka")), Respond(Call(Submit, Submission(claimCitations: ["S2"])))]);

        var assessed = Assert.IsType<AssessmentResult.Assessed>(run.Result);
        Assert.Equal("ka", Assert.Single(assessed.Claims[0].Citations).SectionKey);
    }

    [Fact]
    public async Task The_second_attempt_cannot_cite_a_section_only_the_first_was_given()
    {
        var run = await InvestigateAsync(
            new CountingKnowledge(SectionA),
            [Search("ka")],
            [Respond(Profile()), Respond(Call(Submit, Submission(claimCitations: ["S1"]))), Respond(Call(Submit, Submission(claimCitations: ["S1"])))]);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(run.Result).Reason);
    }

    [Fact]
    public async Task A_reused_read_counts_toward_the_shared_tool_budget_and_reports_progress()
    {
        var run = await InvestigateAsync(new CountingKnowledge(SectionA), [Search("ka")], [Respond(Search("ka")), Respond(Call(Submit, Submission()))]);

        Assert.Equal(2, run.Progress.OfType<InvestigationProgress.ToolStarted>().Count(p => p.Tool == ToolNames.KnowledgeSearch));
        Assert.Equal(2, run.Progress.OfType<InvestigationProgress.ToolFinished>().Count(p => p.Tool == ToolNames.KnowledgeSearch));
        Assert.Equal(2, run.Usage.Tools.Count(t => t.Name == ToolNames.KnowledgeSearch));
    }
}
