using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Investigation;

namespace PaymentOps.Web.SmokeTests;

public sealed class AssessmentViewTests
{
    private static readonly Guid Id = Guid.Parse("6f1d2c3b-0000-4000-8000-000000000013");
    private const string SectionKey = "payout-delay--abc";

    private static AssessmentResult.Assessed Assessed(AssessmentOutcome outcome = AssessmentOutcome.Assessed) => new(
        outcome,
        new Statement("The 9 November Fabrikam file never arrived.", [new EvidenceReference("fact:missing-file:fabrikam-pay:2026-11-09", "No Fabrikam Pay file for 2026-11-09", true)], [new Citation("S1", SectionKey, "Payout delay", "Payout delay > The batch didn't close")]),
        [new Statement("M-1042 settles through Fabrikam Pay.", [new EvidenceReference("merchant:M-1042", "Merchant M-1042", false)], [])],
        new RootCause(new Statement("A settlement outage.", [new EvidenceReference("incident:INC-FAB-1109", "Incident INC-FAB-1109", false)], []), RootCauseLabel.Hypothesis),
        Confidence.Medium,
        [new Statement("Raise it with the processor.", [], [new Citation("S1", SectionKey, "Payout delay", "Payout delay > The batch didn't close")])],
        ["Settlement was unavailable."])
    { InvestigationId = Id };

    private static Task<string> Render(AssessmentResult result) =>
        ComponentHtml.RenderAsync<AssessmentView>(new() { [nameof(AssessmentView.Result)] = result });

    [Fact]
    public async Task An_assessment_shows_each_statement_with_its_support()
    {
        var html = await Render(Assessed());

        Assert.Contains("The 9 November Fabrikam file never arrived.", html, StringComparison.Ordinal);
        Assert.Contains("M-1042 settles through Fabrikam Pay.", html, StringComparison.Ordinal);
        Assert.Contains("Merchant M-1042", html, StringComparison.Ordinal);
        Assert.Contains("No Fabrikam Pay file for 2026-11-09 (computed)", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/sources/{SectionKey}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Hypothesis<", html, StringComparison.Ordinal);
        Assert.Contains("Settlement was unavailable.", html, StringComparison.Ordinal);
        Assert.Contains($"Investigation {Id}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_shows_only_its_notice()
    {
        var refused = new AssessmentResult.Assessed(AssessmentOutcome.Refused, new Statement("This can't be investigated here.", [], []), [], null, Confidence.Low, [], []) { InvestigationId = Id };

        var html = await Render(refused);

        Assert.Contains("This can&#x27;t be investigated here.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidence", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_incomplete_assessment_says_so_first()
    {
        var html = await Render(Assessed(AssessmentOutcome.Incomplete));

        Assert.Contains("Incomplete:", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Incomplete:", StringComparison.Ordinal) < html.IndexOf("The 9 November", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Model_text_is_rendered_as_text()
    {
        var hostile = new AssessmentResult.Assessed(AssessmentOutcome.Assessed, new Statement("<script>x</script><b>bold</b>", [], []), [], null, Confidence.Low, [], []);

        var html = await Render(hostile);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeline_renders_labels_in_order_with_one_live_summary()
    {
        InvestigationProgress[] events =
        [
            new InvestigationProgress.ToolStarted("get_merchant_profile", "Merchant profile · M-1042"),
            new InvestigationProgress.ToolFinished("get_merchant_profile", "Merchant profile · M-1042", true, 3, null),
            new InvestigationProgress.ToolStarted("get_settlement_batches", "Settlement batches · M-1042 · 9 Nov–10 Nov"),
            new InvestigationProgress.ToolFinished("get_settlement_batches", "Settlement batches · M-1042 · 9 Nov–10 Nov", false, 0, "Settlement was unavailable."),
            new InvestigationProgress.Submitting(),
        ];

        var html = await ComponentHtml.RenderAsync<Timeline>(new() { [nameof(Timeline.Events)] = events });

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "aria-live=\"polite\""));
        Assert.True(html.IndexOf("Merchant profile", StringComparison.Ordinal) < html.IndexOf("Settlement batches", StringComparison.Ordinal));
        Assert.Contains("3 records", html, StringComparison.Ordinal);
        Assert.Contains("Settlement was unavailable.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Carried_history_is_announced()
    {
        var html = await ComponentHtml.RenderAsync<ConversationNotice>(new() { [nameof(ConversationNotice.Status)] = new ConversationStatus(5, 2, false) });

        Assert.Contains("The agent sees your last 2 questions", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_history_carried_needs_no_notice()
    {
        var html = await ComponentHtml.RenderAsync<ConversationNotice>(new() { [nameof(ConversationNotice.Status)] = new ConversationStatus(2, 2, false) });

        Assert.DoesNotContain("The agent sees", html, StringComparison.Ordinal);
    }
}
