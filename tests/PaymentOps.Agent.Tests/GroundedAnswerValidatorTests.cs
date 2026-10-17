using System.Text.Json;

using Microsoft.Extensions.AI;

using OpenAI.Chat;

using PaymentOps.Agent.Answering;
using PaymentOps.Agent.Contracts;

using AiChatFinishReason = Microsoft.Extensions.AI.ChatFinishReason;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace PaymentOps.Agent.Tests;

public sealed class GroundedAnswerValidatorTests
{
    private static readonly EvidenceRegistry Registry = EvidenceRegistry.From(
        [EvidenceRegistryTests.Section("key-a", "Doc A", "Doc A > One"), EvidenceRegistryTests.Section("key-b", "Doc B", "Doc B > Two")]);

    public static TheoryData<string> BrokenStates => new()
    {
        Json("answered", "   ", ["S1"], string.Empty),
        Json("answered", "No citations.", [], string.Empty),
        Json("answered", "x [S1]", ["S1"], "but also this"),
        Json("refused", "Here's the answer anyway", [], "insufficient"),
        Json("refused", string.Empty, ["S1"], "insufficient"),
        Json("refused", string.Empty, [], "  "),
    };

    public static TheoryData<string> BrokenProvenance => new()
    {
        Json("answered", "x [S1]", ["S1", "S1"], string.Empty),
        Json("answered", "x [S1] y [S2]", ["S1"], string.Empty),
        Json("answered", "x [S1]", ["S1", "S2"], string.Empty),
        Json("answered", "x [S7]", ["S7"], string.Empty),
        Json("answered", "See [S1](https://evil.example)", ["S1"], string.Empty),
        Json("answered", "See `[S1]`", ["S1"], string.Empty),
        Json("answered", "x [S1] and [s2]", ["S1"], string.Empty),
    };

    internal static string Json(string status, string answer, string[] ids, string reason) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["status"] = status, ["answer_markdown"] = answer, ["citation_ids"] = ids, ["refusal_reason"] = reason });

    internal static ChatResponse Response(string text, AiChatFinishReason? finish = null, object? raw = null) =>
        new(new AiChatMessage(AiChatRole.Assistant, text)) { FinishReason = finish ?? AiChatFinishReason.Stop, RawRepresentation = raw };

    [Fact]
    public void A_valid_answer_carries_registry_citations()
    {
        var result = Validate(Response(Json("answered", "Late files roll forward [S2] and [S1].", ["S2", "S1"], string.Empty)));

        var answered = Assert.IsType<InvestigationResult.Answered>(result);
        Assert.Equal(["S2", "S1"], answered.Citations.Select(c => c.Id));
        Assert.Equal("Doc B > Two", answered.Citations[0].SectionPath);
        Assert.Equal("key-b", answered.Citations[0].SectionKey);
    }

    [Fact]
    public void A_valid_refusal_is_insufficient_evidence() =>
        AssertNotAnswered(NotAnsweredReason.InsufficientEvidence, Validate(Response(Json("refused", string.Empty, [], "Not covered."))));

    [Theory]
    [InlineData("length", NotAnsweredReason.AnswerIncomplete)]
    [InlineData("content_filter", NotAnsweredReason.AnswerBlocked)]
    [InlineData("tool_calls", NotAnsweredReason.AnswerUnverifiable)]
    [InlineData("something_new", NotAnsweredReason.AnswerUnverifiable)]
    public void Anything_but_a_stop_is_not_answered(string finish, NotAnsweredReason reason) =>
        AssertNotAnswered(reason, Validate(Response(Json("answered", "x [S1]", ["S1"], string.Empty), new AiChatFinishReason(finish))));

    [Fact]
    public void A_provider_refusal_is_blocked()
    {
        // The SDK's model factory is marked experimental; it's the supported way to build a completion in tests.
#pragma warning disable OPENAI001
        var refusal = OpenAIChatModelFactory.ChatCompletion(refusal: "I can't help with that.");
#pragma warning restore OPENAI001

        AssertNotAnswered(NotAnsweredReason.AnswerBlocked, Validate(Response(string.Empty, raw: refusal)));
    }

    [Fact]
    public void Empty_output_is_unverifiable() => AssertNotAnswered(NotAnsweredReason.AnswerUnverifiable, Validate(Response(string.Empty)));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"status\":\"answered\",\"answer_markdown\":\"x [S1]\",\"citation_ids\":[\"S1\"]}")]
    [InlineData("{\"status\":\"answered\",\"answer_markdown\":\"x [S1]\",\"citation_ids\":[\"S1\"],\"refusal_reason\":\"\",\"extra\":1}")]
    [InlineData("{\"status\":\"maybe\",\"answer_markdown\":\"x\",\"citation_ids\":[],\"refusal_reason\":\"\"}")]
    [InlineData("{\"status\":\"answered\",\"answer_markdown\":null,\"citation_ids\":[\"S1\"],\"refusal_reason\":\"\"}")]
    public void Anything_but_the_exact_shape_is_unverifiable(string text) =>
        AssertNotAnswered(NotAnsweredReason.AnswerUnverifiable, Validate(Response(text)));

    [Theory]
    [MemberData(nameof(BrokenStates))]
    public void Contradictory_states_are_unverifiable(string json) =>
        AssertNotAnswered(NotAnsweredReason.AnswerUnverifiable, Validate(Response(json)));

    [Theory]
    [MemberData(nameof(BrokenProvenance))]
    public void Any_provenance_mismatch_fails_the_whole_answer(string json) =>
        AssertNotAnswered(NotAnsweredReason.AnswerUnverifiable, Validate(Response(json)));

    [Fact]
    public void The_schema_is_strict_with_every_field_required()
    {
        var schema = GroundedAnswerSchema.Schema;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["status", "answer_markdown", "citation_ids", "refusal_reason"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["answered", "refused"], schema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    private static InvestigationResult Validate(ChatResponse response) => GroundedAnswerValidator.Validate(response, Registry);

    private static void AssertNotAnswered(NotAnsweredReason reason, InvestigationResult result) =>
        Assert.Equal(reason, Assert.IsType<InvestigationResult.NotAnswered>(result).Reason);
}
