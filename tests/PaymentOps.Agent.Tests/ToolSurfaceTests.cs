using Microsoft.Extensions.AI;

using PaymentOps.Tools;

using static PaymentOps.Agent.Tests.AgentFixture;
using static PaymentOps.Agent.Tests.ScriptedChatClient;

namespace PaymentOps.Agent.Tests;

/// <summary>What the model can call is the five read tools and submit_assessment: no hosted tools such as web search.</summary>
public sealed class ToolSurfaceTests
{
    // Spelled out rather than read from ToolNames, so adding a tool to the agent has to change this test too.
    private static readonly string[] Offered =
    [
        "get_merchant_profile", "get_settlement_batches", "get_processor_status", "get_error_events", "search_knowledge", "submit_assessment",
    ];

    [Fact]
    public async Task Every_request_offers_exactly_the_read_tools_and_submit()
    {
        using var fixture = new AgentFixture();
        var model = new ScriptedChatClient(
        [
            _ => Call(ToolNames.MerchantProfile, new { merchant_id = "M-1042" }),
            _ => Call(ToolNames.SubmitAssessment, Submission()),
        ]);

        await fixture.Runner(model).InvestigateAsync(RunnerExtensions.Question, CancellationToken.None);

        Assert.Equal(2, model.Requests.Count);
        Assert.All(model.Requests, request =>
        {
            Assert.Equal(Offered.Order(StringComparer.Ordinal), request.Tools.Order(StringComparer.Ordinal));
            Assert.All(request.Options!.Tools!, tool => Assert.IsAssignableFrom<AIFunction>(tool));
        });
    }
}
