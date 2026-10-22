using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Approvals;
using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Conversations;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Knowledge;
using PaymentOps.Knowledge.Indexing;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

public sealed partial class ConversationTests
{
    private const string Submit = ToolNames.SubmitAssessment;

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Respond(ChatResponse response) => _ => response;

    private static AssessmentResult.Assessed Assessed(string summary = "M-1042's Fabrikam file for 9 November didn't arrive.") => new(
        AssessmentOutcome.Assessed,
        new Statement(summary, [new EvidenceReference("fact:missing-file:fabrikam-pay:2026-11-09", "No file", true)], [new Citation("S1", "key", "Payout delay caused by a processor outage", "Payout delay > The batch didn't close")]),
        [new Statement("The merchant is routed to Fabrikam Pay.", [new EvidenceReference("merchant:M-1042", "M-1042", false)], [])],
        new RootCause(new Statement("A settlement outage.", [new EvidenceReference("incident:INC-FAB-1109", "INC-FAB-1109", false)], [new Citation("S2", "key2", "Outage runbook", "Outage runbook > Act")]), RootCauseLabel.Hypothesis),
        Confidence.Medium,
        [],
        ["Settlement was unavailable."])
    { InvestigationId = Guid.NewGuid() };

    private static ConversationTurn Turn(int i, AssessmentResult? result = null) => new($"Question {i}?", result ?? Assessed());

    [Fact]
    public void Projection_never_contains_tool_messages_or_section_ids()
    {
        var (messages, _) = HistoryProjection.Build([Turn(1), Turn(2, new AssessmentResult.NotAssessed(NotAssessedReason.LimitReached))], 100_000, _ => null);

        Assert.All(messages, m => Assert.Contains(m.Role, new[] { ChatRole.User, ChatRole.Assistant }));
        Assert.All(messages, m => Assert.All(m.Contents, c => Assert.IsType<TextContent>(c)));
        Assert.DoesNotMatch(SectionId(), string.Join("\n", messages.Select(m => m.Text)));
        Assert.Contains("(source: Payout delay caused by a processor outage)", messages[1].Text, StringComparison.Ordinal);
        Assert.Contains("merchant:M-1042", messages[1].Text, StringComparison.Ordinal);
        Assert.Contains("Root cause (hypothesis): A settlement outage.", messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_earlier_turn_is_marked_as_context_not_evidence()
    {
        var (messages, _) = HistoryProjection.Build([Turn(1), Turn(2, new AssessmentResult.NotAssessed(NotAssessedReason.LimitReached))], 100_000, _ => null);

        Assert.Equal(["Question 1?", "Question 2?"], messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.All(messages.Where(m => m.Role == ChatRole.Assistant), m => Assert.StartsWith("Earlier context, not evidence for this question.", m.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_newest_turns_that_fit_are_carried()
    {
        var turns = Enumerable.Range(1, 5).Select(i => Turn(i)).ToList();
        var oneTurn = HistoryProjection.Build([turns[0]], 100_000, _ => null).Messages.Sum(m => m.Text.Length);

        var (messages, carried) = HistoryProjection.Build(turns, (2 * oneTurn) + 10, _ => null);

        Assert.Equal(2, carried);
        Assert.Equal(["Question 4?", "Question 5?"], messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
    }

    [Fact]
    public void The_approval_note_is_carried_with_its_turn()
    {
        var turn = Turn(1);

        var (messages, _) = HistoryProjection.Build([turn], 100_000, id => id == turn.Result.InvestigationId ? "Ticket INC-0001 was created." : null);

        Assert.EndsWith("Ticket INC-0001 was created.", messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_budget_reserves_the_current_turn_first()
    {
        var budget = HistoryProjection.HistoryBudgetChars(new ConversationOptions(), new InvestigationLimits(), fixedPromptChars: 12_000);

        // (32,000 − 4,000 − ⌈1,000 / 3⌉ − 10 × 2,300 − 2,000) × 3
        Assert.Equal(7_998, budget);
    }

    [Fact]
    public async Task A_follow_up_cannot_cite_evidence_from_an_earlier_turn()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
            Respond(Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" })),
            Respond(Call(Submit, Submission())),
            Respond(Call(Submit, Submission())),
            Respond(Call(Submit, Submission())));
        var (service, _) = Service(fixture, model);

        Assert.IsType<AssessmentResult.Assessed>(await service.InvestigateAsync("Why was M-1042 late?", null, CancellationToken.None));
        var followUp = await service.InvestigateAsync("And what about M-1042's batch?", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.AssessmentUnverified, Assert.IsType<AssessmentResult.NotAssessed>(followUp).Reason);
        Assert.Contains(ToolResults(model.Requests[3].Messages), r => r.Contains("'merchant:M-1042' wasn't returned by a tool", StringComparison.Ordinal));
        Assert.Contains(model.Requests[2].Messages, m => m.Role == ChatRole.Assistant && m.Text.StartsWith("Earlier context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_conversation_ends_at_twenty_questions()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient();
        var (service, conversation) = Service(fixture, model);
        for (var i = 0; i < 20; i++)
        {
            conversation.Add(new ConversationTurn($"Q{i}", new AssessmentResult.NotAssessed(NotAssessedReason.LimitReached)));
        }

        var result = await service.InvestigateAsync("One more?", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.ConversationLimit, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Empty(model.Requests);
        Assert.True(service.Conversation.Ended);
    }

    [Fact]
    public async Task The_conversation_ends_when_the_newest_turn_alone_does_not_fit()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient();
        var (service, conversation) = Service(fixture, model);
        conversation.Add(Turn(1, Assessed(new string('x', 40_000))));

        var result = await service.InvestigateAsync("One more?", null, CancellationToken.None);

        Assert.Equal(NotAssessedReason.ConversationLimit, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Empty(model.Requests);
        Assert.True(service.Conversation.Ended);
    }

    [Fact]
    public async Task A_cancelled_or_invalid_question_is_not_recorded()
    {
        using var fixture = new AgentFixture();
        var (service, conversation) = Service(fixture, new ScriptedChatClient());

        await service.InvestigateAsync(" ", null, CancellationToken.None);

        Assert.Empty(conversation.Turns);
    }

    [Fact]
    public void Starting_new_clears_every_turn()
    {
        using var fixture = new AgentFixture();
        var (service, conversation) = Service(fixture, new ScriptedChatClient());
        conversation.Add(Turn(1));
        var before = conversation.Id;

        service.StartNew();

        Assert.Equal(new ConversationStatus(0, 0, false), service.Conversation);
        Assert.NotEqual(before, conversation.Id);
    }

    [Fact]
    public void The_status_counts_the_questions_the_next_one_would_carry()
    {
        using var fixture = new AgentFixture();
        var (service, conversation) = Service(fixture, new ScriptedChatClient());
        conversation.Add(Turn(1));
        conversation.Add(Turn(2));

        Assert.Equal(new ConversationStatus(2, 2, false), service.Conversation);
    }

    // The history budget assumes every tool result fits its allowance; this holds the shipped data to that.
    [Fact]
    public async Task Each_tool_result_fits_its_allowance_on_the_shipped_dataset()
    {
        var dataset = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, "data", "datasets", "default", "dataset.json"))).RootElement;
        var merchants = dataset.GetProperty("merchants").EnumerateArray().Select(m => m.GetProperty("id").GetString()!).ToList();
        var processors = dataset.GetProperty("processors").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToList();
        var largest = IndexedCorpus.FromSourceDirectory(Path.Combine(RepositoryRoot.FullPath, "data", "knowledge", "src"))
            .OrderByDescending(d => d.Content.Length).Take(KnowledgeRetriever.Top)
            .Select(d => new RetrievedSection(d.Id, d.DocId, d.Title, d.SectionPath, d.Content, d.Classification, null)).ToArray();
        using var fixture = new AgentFixture(knowledge: FakeKnowledgeLookup.Returning(largest));
        const string Start = "2026-10-12";
        const string End = "2026-11-11";
        List<(string Tool, Dictionary<string, object?> Arguments)> calls =
        [
            .. merchants.Select(m => (ToolNames.MerchantProfile, new Dictionary<string, object?> { ["merchant_id"] = m })),
            .. merchants.Select(m => (ToolNames.SettlementBatches, new Dictionary<string, object?> { ["merchant_id"] = m, ["start_date"] = Start, ["end_date"] = End })),
            .. merchants.Select(m => (ToolNames.ErrorEvents, new Dictionary<string, object?> { ["merchant_id"] = m, ["start_date"] = Start, ["end_date"] = End })),
            .. processors.Select(p => (ToolNames.ProcessorStatus, new Dictionary<string, object?> { ["processor_id"] = p, ["start_date"] = Start, ["end_date"] = End })),
            .. processors.Select(p => (ToolNames.ErrorEvents, new Dictionary<string, object?> { ["processor_id"] = p, ["start_date"] = Start, ["end_date"] = End })),
            (ToolNames.KnowledgeSearch, new Dictionary<string, object?> { ["query"] = "settlement" }),
        ];
        var allowanceChars = new ConversationOptions().ToolResultAllowanceTokens * HistoryProjection.CharsPerToken;

        foreach (var (tool, arguments) in calls)
        {
            var guard = new ToolGuard(new TurnState(), new InvestigationLimits(), TimeProvider.System, TestUser.RestrictedReader, null);
            var function = guard.Wrap(fixture.Tool(tool));
            var json = (string)(await function.InvokeAsync(new AIFunctionArguments(arguments)))!;

            Assert.True(json.Length <= allowanceChars, $"{tool} {string.Join(',', arguments.Values)}: {json.Length} chars > {allowanceChars}.");
        }
    }

    private static (InvestigationService Service, Conversation Conversation) Service(AgentFixture fixture, ScriptedChatClient model)
    {
        var conversation = new Conversation();
        var limits = Options.Create(new InvestigationLimits());
        var service = new InvestigationService(
            fixture.Runner(model),
            new SourceService(FakeKnowledgeLookup.Returning(), TestUser.Reader, limits, NullLogger<SourceService>.Instance),
            conversation,
            Options.Create(new ConversationOptions()),
            limits,
            TestUser.Reader,
            new ProposalStore(TimeProvider.System, Options.Create(new ApprovalOptions()), AiReleaseLoader.Load(new EmbeddedAiReleaseSource())));
        return (service, conversation);
    }

    [GeneratedRegex(@"\bS\d+\b")]
    private static partial Regex SectionId();
}
