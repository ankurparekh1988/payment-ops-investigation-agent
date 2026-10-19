using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Pages;

namespace PaymentOps.Web.SmokeTests;

public sealed class AskPageTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task The_ask_page_has_a_question_box_and_no_retrieval_settings()
    {
        using var client = factory.CreateReaderClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<textarea", html, StringComparison.Ordinal);
        foreach (var hidden in new[] { "lexical", "semantic", "knowledge-v1", "semanticFloor", "rerank" })
        {
            Assert.DoesNotContain(hidden, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(NotAnsweredReason.NoEvidence)]
    [InlineData(NotAnsweredReason.InsufficientEvidence)]
    public void Missing_and_insufficient_evidence_read_the_same(NotAnsweredReason reason) =>
        Assert.Equal("I couldn't find this in the documents you can access.", NotAnsweredText.For(reason, 1000));

    [Fact]
    public void Every_reason_has_fixed_text() =>
        Assert.All(Enum.GetValues<NotAnsweredReason>(), r => Assert.False(string.IsNullOrWhiteSpace(NotAnsweredText.For(r, 1000))));

    [Fact]
    public void Busy_says_a_question_is_already_running() =>
        Assert.Equal("Another question is already being processed.", NotAnsweredText.For(NotAnsweredReason.Busy, 1000));

    [Fact]
    public void An_invalid_question_states_the_configured_limit() =>
        Assert.Contains("2,000", NotAnsweredText.For(NotAnsweredReason.InvalidQuestion, 2000), StringComparison.Ordinal);
}
