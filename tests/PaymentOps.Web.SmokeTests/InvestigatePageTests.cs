using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Pages;

namespace PaymentOps.Web.SmokeTests;

public sealed class InvestigatePageTests(PaymentOpsWebApplicationFactory factory) : IClassFixture<PaymentOpsWebApplicationFactory>
{
    [Fact]
    public async Task The_home_page_is_the_investigation_page()
    {
        using var client = factory.CreateReaderClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<h1>Investigate</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<textarea", html, StringComparison.Ordinal);
        foreach (var hidden in new[] { "lexical", "semantic", "knowledge-v1", "semanticFloor", "rerank" })
        {
            Assert.DoesNotContain(hidden, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_not_assessed_reason_has_fixed_text() =>
        Assert.All(Enum.GetValues<NotAssessedReason>(), r => Assert.False(string.IsNullOrWhiteSpace(NotAssessedText.For(r, 1000))));

    [Fact]
    public void A_blocked_investigation_shows_the_safety_notice()
    {
        var text = NotAssessedText.For(NotAssessedReason.Blocked, 1000);

        Assert.Equal("This question can't be investigated: it was blocked by the content safety policy.", text);
        Assert.DoesNotContain("isn't responding", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_question_states_the_configured_limit() =>
        Assert.Contains("2,000", NotAssessedText.For(NotAssessedReason.InvalidQuestion, 2000), StringComparison.Ordinal);
}
