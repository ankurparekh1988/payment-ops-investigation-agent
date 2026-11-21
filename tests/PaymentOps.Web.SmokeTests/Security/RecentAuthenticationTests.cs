using System.Globalization;

using PaymentOps.Web.Security;

namespace PaymentOps.Web.SmokeTests.Security;

public sealed class RecentAuthenticationTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Theory]
    [InlineData(-899, true)]
    [InlineData(0, true)]
    [InlineData(120, true)]
    [InlineData(-900, false)]
    [InlineData(121, false)]
    public void Freshness_rule(int offsetSeconds, bool expected) =>
        Assert.Equal(expected, RecentAuthentication.IsFresh(T.AddSeconds(offsetSeconds), T, Window));

    [Fact]
    public void A_missing_authentication_time_is_stale() =>
        Assert.False(RecentAuthentication.IsFresh(null, T, Window));

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("253402300800")]
    [InlineData("9223372036854775807")]
    [InlineData("99999999999999999999")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("1.5")]
    public void Unusable_claims_parse_to_null(string? claim) =>
        Assert.Null(RecentAuthentication.Parse(claim));

    [Fact]
    public void Unix_seconds_parse_to_a_time()
    {
        var seconds = T.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        Assert.Equal(T, RecentAuthentication.Parse(seconds));
        Assert.Equal(DateTimeOffset.UnixEpoch, RecentAuthentication.Parse("0"));
        Assert.NotNull(RecentAuthentication.Parse("253402300799"));
    }

    [Theory]
    [InlineData(-300, true)]
    [InlineData(-301, false)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Step_up_age(int offsetSeconds, bool expected) =>
        Assert.Equal(expected, RecentAuthentication.IsJustAuthenticated(T.AddSeconds(offsetSeconds), T));

    [Fact]
    public void A_missing_step_up_time_is_not_recent() =>
        Assert.False(RecentAuthentication.IsJustAuthenticated(null, T));

    [Fact]
    public void Fingerprint_ignores_order_and_case_of_groups_and_differs_on_any_change()
    {
        var baseline = RecentAuthentication.Fingerprint(["Ops.Reader", "Ops.Approver"], ["AAA", "bbb"]);

        Assert.Equal(baseline, RecentAuthentication.Fingerprint(["Ops.Approver", "Ops.Reader", "Ops.Reader"], ["BBB", "aaa", "aaa"]));
        Assert.NotEqual(baseline, RecentAuthentication.Fingerprint(["Ops.Reader"], ["aaa", "bbb"]));
        Assert.NotEqual(baseline, RecentAuthentication.Fingerprint(["Ops.Reader", "Ops.Approver"], ["aaa"]));
        Assert.NotEqual(baseline, RecentAuthentication.Fingerprint(["Ops.Reader", "Ops.Approver", "Ops.Admin"], ["aaa", "bbb"]));
        Assert.NotEqual(baseline, RecentAuthentication.Fingerprint(["ops.reader", "Ops.Approver"], ["aaa", "bbb"]));
        Assert.NotEqual(RecentAuthentication.Fingerprint(["x"], []), RecentAuthentication.Fingerprint([], ["x"]));
    }
}
