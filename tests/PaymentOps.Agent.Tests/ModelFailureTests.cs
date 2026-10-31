using System.ClientModel;

using Microsoft.Extensions.AI;

using PaymentOps.Agent.Contracts;
using PaymentOps.Agent.Investigating;
using PaymentOps.Agent.Model;

namespace PaymentOps.Agent.Tests;

public sealed class ModelFailureTests
{
    internal static ClientResultException ContentFilterRejection() =>
        new(new FakePipelineResponse(400, "{\"error\":{\"code\":\"content_filter\",\"message\":\"filtered\"}}"));

    [Fact]
    public void A_content_filter_rejection_is_recognised() => Assert.True(ModelFailure.IsContentFilterRejection(ContentFilterRejection()));

    [Fact]
    public void Other_bad_requests_are_not() =>
        Assert.False(ModelFailure.IsContentFilterRejection(new ClientResultException(new FakePipelineResponse(400, "{\"error\":{\"code\":\"invalid_request\"}}"))));

    [Fact]
    public async Task A_content_filter_rejection_is_blocked()
    {
        using var fixture = new AgentFixture();
        var logger = new RecordingLogger<InvestigationAgentRunner>();
        var model = new ScriptedChatClient(_ => throw ContentFilterRejection());

        var result = await fixture.Runner(model, logger: logger).InvestigateAsync("q", CancellationToken.None);

        Assert.Equal(NotAssessedReason.Blocked, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Single(model.Requests);
        Assert.Contains(logger.Entries, e => e.Message.Contains("blocked by the content filter on the prompt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_filtered_completion_is_blocked()
    {
        using var fixture = new AgentFixture();
        var logger = new RecordingLogger<InvestigationAgentRunner>();
        var model = ContentFilter.Checked(new ScriptedChatClient(_ => Filtered()));

        var result = await fixture.Runner(model, logger: logger).InvestigateAsync("q", CancellationToken.None);

        Assert.Equal(NotAssessedReason.Blocked, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
        Assert.Contains(logger.Entries, e => e.Message.Contains("blocked by the content filter on the completion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Other_400s_stay_service_failures()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(_ => throw new ClientResultException(new FakePipelineResponse(400, "{\"error\":{\"code\":\"invalid_request\"}}")));

        var result = await fixture.Runner(model).InvestigateAsync("q", CancellationToken.None);

        Assert.Equal(NotAssessedReason.ModelUnavailable, Assert.IsType<AssessmentResult.NotAssessed>(result).Reason);
    }

    [Fact]
    public async Task A_completion_that_finished_normally_passes_the_check()
    {
        var model = ContentFilter.Checked(new ScriptedChatClient(_ => ScriptedChatClient.Text("ok")));

        Assert.Equal("ok", (await model.GetResponseAsync("q")).Text);
    }

    internal static ChatResponse Filtered() => new(new ChatMessage(ChatRole.Assistant, string.Empty)) { FinishReason = ChatFinishReason.ContentFilter };

    [Fact]
    public void A_body_that_isnt_json_has_no_error_code() =>
        Assert.Null(ModelFailure.ErrorCode(new ClientResultException(new FakePipelineResponse(500, "<html>"))));
}
