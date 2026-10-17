using System.ClientModel;

using PaymentOps.Agent.Answering;

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
    public void A_body_that_isnt_json_has_no_error_code() =>
        Assert.Null(ModelFailure.ErrorCode(new ClientResultException(new FakePipelineResponse(500, "<html>"))));
}
