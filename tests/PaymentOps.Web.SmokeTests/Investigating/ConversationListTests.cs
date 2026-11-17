using Bunit;

using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Investigation;
using PaymentOps.Web.Investigating;

namespace PaymentOps.Web.SmokeTests.Investigating;

/// <summary>
/// The list shows every question asked in this tab, but the conversation keeps only some: a cancelled, invalid or
/// unauthorised question isn't carried into the next one, and the list says so.
/// </summary>
public sealed class ConversationListTests : InteractiveTestContext
{
    [Theory]
    [InlineData(NotAssessedReason.Cancelled, "Cancelled · not kept")]
    [InlineData(NotAssessedReason.InvalidQuestion, "Not assessed · not kept")]
    [InlineData(NotAssessedReason.NotAuthorized, "Not assessed · not kept")]
    [InlineData(NotAssessedReason.ModelUnavailable, "Not assessed")]
    [InlineData(NotAssessedReason.Blocked, "Not assessed")]
    public void A_question_the_conversation_does_not_keep_is_labelled(NotAssessedReason reason, string outcome)
    {
        SessionTurn[] turns = [new("Why was M-1042 not paid?", new AssessmentResult.NotAssessed(reason) { InvestigationId = Guid.NewGuid() })];

        var list = Render<ConversationList>(p => p.Add(c => c.Turns, turns));

        Assert.Equal(outcome, list.Find(".conversation-outcome").TextContent);
    }

    [Fact]
    public void An_assessed_question_is_kept()
    {
        SessionTurn[] turns = [new("Why was M-1042 not paid?", Results.WithPendingTicket())];

        var list = Render<ConversationList>(p => p.Add(c => c.Turns, turns));

        Assert.Equal("Assessed", list.Find(".conversation-outcome").TextContent);
    }
}
