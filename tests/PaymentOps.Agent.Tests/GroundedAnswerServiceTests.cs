using System.Net;
using System.Text.Json;

using Azure;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;
using PaymentOps.Knowledge.Retrieval;
using PaymentOps.Platform.Ai;

namespace PaymentOps.Agent.Tests;

public sealed class GroundedAnswerServiceTests
{
    private static readonly RetrievedSection SectionA = EvidenceRegistryTests.Section("key-a", "Doc A", "Doc A > One");
    private static readonly AiRelease Release = AiReleaseLoader.Load(new EmbeddedAiReleaseSource());

    [Fact]
    public async Task An_answer_from_the_evidence_is_returned_with_its_citation()
    {
        var result = await Service(FakeKnowledgeLookup.Returning(SectionA), Answering()).AskAsync("Where is it?", CancellationToken.None);

        Assert.Equal("key-a", Assert.Single(Assert.IsType<InvestigationResult.Answered>(result).Citations).SectionKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_question_never_reaches_search_or_the_model(string question)
    {
        var knowledge = FakeKnowledgeLookup.Returning(SectionA);
        var chat = Answering();

        Assert.Equal(NotAnsweredReason.InvalidQuestion, Reason(await Service(knowledge, chat).AskAsync(question, CancellationToken.None)));
        Assert.Equal(0, knowledge.Retrievals);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task A_question_over_the_limit_is_invalid()
    {
        var chat = Answering();

        var result = await Service(FakeKnowledgeLookup.Returning(SectionA), chat, options: new AnsweringOptions { MaxQuestionLength = 10 }).AskAsync(new string('x', 11), CancellationToken.None);

        Assert.Equal(NotAnsweredReason.InvalidQuestion, Reason(result));
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task An_unauthenticated_user_is_not_authorized()
    {
        var knowledge = FakeKnowledgeLookup.Returning(SectionA);
        var chat = Answering();

        Assert.Equal(NotAnsweredReason.NotAuthorized, Reason(await Service(knowledge, chat, TestUser.Anonymous).AskAsync("q", CancellationToken.None)));
        Assert.Equal(0, knowledge.Retrievals);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Zero_hits_never_call_the_model()
    {
        var chat = Answering();

        Assert.Equal(NotAnsweredReason.NoEvidence, Reason(await Service(FakeKnowledgeLookup.Returning(), chat).AskAsync("q", CancellationToken.None)));
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Search_unavailable_never_calls_the_model()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => throw new KnowledgeUnavailableException("down"));
        var chat = Answering();

        Assert.Equal(NotAnsweredReason.KnowledgeUnavailable, Reason(await Service(knowledge, chat).AskAsync("q", CancellationToken.None)));
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Our_search_timeout_is_unavailable_not_cancellation()
    {
        var knowledge = new FakeKnowledgeLookup(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        var chat = Answering();

        var result = await Service(knowledge, chat, options: new AnsweringOptions { SearchTimeout = TimeSpan.FromMilliseconds(50) }).AskAsync("q", CancellationToken.None);

        Assert.Equal(NotAnsweredReason.KnowledgeUnavailable, Reason(result));
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_during_search_propagates()
    {
        using var caller = new CancellationTokenSource();
        var knowledge = new FakeKnowledgeLookup(async (_, ct) =>
        {
            await caller.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return [];
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(knowledge, Answering()).AskAsync("q", caller.Token));
    }

    [Fact]
    public async Task Insufficient_evidence_is_the_models_refusal()
    {
        var chat = FakeChatClient.Returning(GroundedAnswerValidatorTests.Response(GroundedAnswerValidatorTests.Json("refused", string.Empty, [], "Not covered.")));

        Assert.Equal(NotAnsweredReason.InsufficientEvidence, Reason(await Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", CancellationToken.None)));
        Assert.Equal(1, chat.Calls);
    }

    [Fact]
    public async Task A_model_failure_is_unavailable()
    {
        var chat = new FakeChatClient((_, _, _) => throw new HttpRequestException("reset"));

        Assert.Equal(NotAnsweredReason.AnswerUnavailable, Reason(await Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", CancellationToken.None)));
    }

    [Fact]
    public async Task Our_model_timeout_is_unavailable()
    {
        var chat = new FakeChatClient(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        var result = await Service(FakeKnowledgeLookup.Returning(SectionA), chat, options: new AnsweringOptions { ModelTimeout = TimeSpan.FromMilliseconds(50) }).AskAsync("q", CancellationToken.None);

        Assert.Equal(NotAnsweredReason.AnswerUnavailable, Reason(result));
    }

    [Fact]
    public async Task A_request_rejected_by_content_filtering_is_blocked()
    {
        var chat = new FakeChatClient((_, _, _) => throw ModelFailureTests.ContentFilterRejection());

        Assert.Equal(NotAnsweredReason.AnswerBlocked, Reason(await Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", CancellationToken.None)));
    }

    [Fact]
    public async Task An_unexpected_failure_stays_an_exception()
    {
        var chat = new FakeChatClient((_, _, _) => throw new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_during_the_model_call_propagates()
    {
        using var caller = new CancellationTokenSource();
        var chat = new FakeChatClient(async (_, _, ct) =>
        {
            await caller.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", caller.Token));
    }

    [Fact]
    public async Task Retrieval_uses_the_users_access()
    {
        var knowledge = FakeKnowledgeLookup.Returning(SectionA);

        await Service(knowledge, Answering(), TestUser.Restricted).AskAsync("q", CancellationToken.None);

        Assert.True(knowledge.LastAccess!.IncludesRestricted);
    }

    [Fact]
    public async Task The_wire_request_is_strict_structured_output_with_no_reasoning_and_a_token_limit()
    {
        var completion = JsonSerializer.Serialize(new
        {
            id = "c1",
            @object = "chat.completion",
            created = 1,
            model = "chat-blue",
            choices = new[] { new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = GroundedAnswerValidatorTests.Json("answered", "Text [S1].", ["S1"], string.Empty) } } },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
        });
        var handler = new StubModelHandler(HttpStatusCode.OK, completion);
        var credential = new StaticTokenCredential();
        var chat = ModelClient.Create(new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default", credential, "chat-blue", handler);

        var result = await Service(FakeKnowledgeLookup.Returning(SectionA), chat, options: new AnsweringOptions { MaxOutputTokens = 321 }).AskAsync("q", CancellationToken.None);

        Assert.IsType<InvestigationResult.Answered>(result);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://foundry.example/openai/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
        Assert.Contains("https://cognitiveservices.azure.com/.default", credential.Scopes);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("chat-blue", json.GetProperty("model").GetString());
        Assert.Equal("none", json.GetProperty("reasoning_effort").GetString());
        Assert.Equal(321, json.GetProperty("max_completion_tokens").GetInt32());
        var format = json.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal(GroundedAnswerSchema.Name, format.GetProperty("json_schema").GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_unreachable_model_endpoint_is_unavailable()
    {
        var chat = ModelClient.Create(new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default", new StaticTokenCredential(), "chat-blue", new ThrowingHandler());

        Assert.Equal(NotAnsweredReason.AnswerUnavailable, Reason(await Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", CancellationToken.None)));
    }

    [Fact]
    public async Task A_failed_token_request_for_the_model_is_unavailable()
    {
        var chat = ModelClient.Create(new Uri("https://foundry.example/"), "https://cognitiveservices.azure.com/.default", new FailingTokenCredential(), "chat-blue", new ThrowingHandler());

        Assert.Equal(NotAnsweredReason.AnswerUnavailable, Reason(await Service(FakeKnowledgeLookup.Returning(SectionA), chat).AskAsync("q", CancellationToken.None)));
    }

    [Fact]
    public async Task Failure_logs_never_carry_the_exception_or_the_question()
    {
        const string Question = "Where is the batch for merchant M-1042?";
        var logger = new RecordingLogger<GroundedAnswerService>();
        var knowledge = new FakeKnowledgeLookup((_, _) => throw new KnowledgeUnavailableException($"Search echoed the query: {Question}"));
        var service = new GroundedAnswerService(knowledge, Answering(), new PromptProvider(Release), Release, TestUser.General, Options.Create(new AnsweringOptions()), logger);

        await service.AskAsync(Question, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(Question, entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(KnowledgeUnavailableException), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_the_user_may_see_is_found()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([])) { Find = (key, _, _) => Task.FromResult(key == "key-a" ? SectionA : null) };

        var found = Assert.IsType<SourceResult.Found>(await Service(knowledge, Answering()).GetSourceAsync("key-a", CancellationToken.None));

        Assert.Equal("Doc A > One", found.SectionPath);
    }

    [Fact]
    public async Task An_unauthenticated_source_request_is_not_found()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([])) { Find = (_, _, _) => Task.FromResult<RetrievedSection?>(SectionA) };

        Assert.IsType<SourceResult.NotFound>(await Service(knowledge, Answering(), TestUser.Anonymous).GetSourceAsync("key-a", CancellationToken.None));
    }

    [Fact]
    public async Task Search_unavailable_for_a_source_is_unavailable()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([])) { Find = (_, _, _) => throw new KnowledgeUnavailableException("down") };

        Assert.IsType<SourceResult.Unavailable>(await Service(knowledge, Answering()).GetSourceAsync("key-a", CancellationToken.None));
    }

    [Fact]
    public async Task A_source_lookup_that_outlasts_our_search_timeout_is_unavailable()
    {
        var knowledge = new FakeKnowledgeLookup((_, _) => Task.FromResult<IReadOnlyList<RetrievedSection>>([]))
        {
            Find = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            },
        };

        var result = await Service(knowledge, Answering(), options: new AnsweringOptions { SearchTimeout = TimeSpan.FromMilliseconds(50) }).GetSourceAsync("key-a", CancellationToken.None);

        Assert.IsType<SourceResult.Unavailable>(result);
    }

    private static GroundedAnswerService Service(FakeKnowledgeLookup knowledge, Microsoft.Extensions.AI.IChatClient chat, TestUser? user = null, AnsweringOptions? options = null) =>
        new(knowledge, chat, new PromptProvider(Release), Release, user ?? TestUser.General, Options.Create(options ?? new AnsweringOptions()), NullLogger<GroundedAnswerService>.Instance);

    private static FakeChatClient Answering() =>
        FakeChatClient.Returning(GroundedAnswerValidatorTests.Response(GroundedAnswerValidatorTests.Json("answered", "Text [S1].", ["S1"], string.Empty)));

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known.");
    }

    private static NotAnsweredReason Reason(InvestigationResult result) => Assert.IsType<InvestigationResult.NotAnswered>(result).Reason;
}
