using PaymentOps.Agent.Contracts;
using PaymentOps.Web.Components.Investigation;
using PaymentOps.Web.Components.Pages;

namespace PaymentOps.Web.SmokeTests;

/// <summary>
/// A result that isn't an assessment is grouped by what the person can do about it, with the fixed text for its reason.
/// The self-harm notice stays calm: no error colour, no icon, nothing to retry.
/// </summary>
public sealed class NotAssessedPanelTests
{
    [Theory]
    [InlineData(NotAssessedReason.InvalidQuestion, NotAssessedGroup.YouCanFixIt)]
    [InlineData(NotAssessedReason.LimitReached, NotAssessedGroup.YouCanFixIt)]
    [InlineData(NotAssessedReason.AssessmentUnverified, NotAssessedGroup.YouCanFixIt)]
    [InlineData(NotAssessedReason.ConversationLimit, NotAssessedGroup.YouCanFixIt)]
    [InlineData(NotAssessedReason.ModelUnavailable, NotAssessedGroup.TryAgainShortly)]
    [InlineData(NotAssessedReason.Unscreened, NotAssessedGroup.TryAgainShortly)]
    [InlineData(NotAssessedReason.UsageUnavailable, NotAssessedGroup.TryAgainShortly)]
    [InlineData(NotAssessedReason.Busy, NotAssessedGroup.TryAgainShortly)]
    [InlineData(NotAssessedReason.Blocked, NotAssessedGroup.LimitOrPolicy)]
    [InlineData(NotAssessedReason.Paused, NotAssessedGroup.LimitOrPolicy)]
    [InlineData(NotAssessedReason.DailyLimitReached, NotAssessedGroup.LimitOrPolicy)]
    [InlineData(NotAssessedReason.Cancelled, NotAssessedGroup.Cancelled)]
    [InlineData(NotAssessedReason.NotAuthorized, NotAssessedGroup.SignedOut)]
    [InlineData(NotAssessedReason.BlockedSelfHarm, NotAssessedGroup.Support)]
    public void Each_reason_is_in_its_group(NotAssessedReason reason, NotAssessedGroup group) =>
        Assert.Equal(group, NotAssessedGroups.For(reason));

    [Fact]
    public void Every_reason_has_a_group() =>
        Assert.All(Enum.GetValues<NotAssessedReason>(), reason => Assert.True(Enum.IsDefined(NotAssessedGroups.For(reason))));

    private static Task<string> Render(NotAssessedReason reason) =>
        ComponentHtml.RenderAsync<NotAssessedPanel>(new()
        {
            [nameof(NotAssessedPanel.Reason)] = reason,
            [nameof(NotAssessedPanel.MaxQuestionLength)] = 2000,
        });

    [Fact]
    public async Task Self_harm_panel_has_no_error_styling_or_retry()
    {
        var html = await Render(NotAssessedReason.BlockedSelfHarm);

        Assert.Contains("contact local emergency services", html, StringComparison.Ordinal);
        Assert.Contains("notice-neutral", html, StringComparison.Ordinal);
        Assert.DoesNotContain("notice-error", html, StringComparison.Ordinal);
        Assert.DoesNotContain("notice-warning", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NotAssessedReason.InvalidQuestion, "Edit question")]
    [InlineData(NotAssessedReason.ConversationLimit, "New investigation")]
    [InlineData(NotAssessedReason.ModelUnavailable, "Try again")]
    [InlineData(NotAssessedReason.NotAuthorized, "Sign in")]
    public async Task A_group_offers_its_one_action(NotAssessedReason reason, string action)
    {
        var html = await Render(reason);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<button"));
        Assert.Contains($">{action}<", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NotAssessedReason.Paused)]
    [InlineData(NotAssessedReason.DailyLimitReached)]
    [InlineData(NotAssessedReason.Blocked)]
    [InlineData(NotAssessedReason.Cancelled)]
    public async Task A_limit_policy_or_cancellation_offers_no_action(NotAssessedReason reason)
    {
        var html = await Render(reason);

        Assert.Contains(NotAssessedText.For(reason, 2000), html.Replace("&#x27;", "'", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_panel_is_a_status_message()
    {
        var html = await Render(NotAssessedReason.ModelUnavailable);

        Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
    }
}
