namespace PaymentOps.Tools.Tests;

public sealed class ToolArgumentsTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    [Fact]
    public void A_31_day_range_is_accepted_and_both_ends_are_included()
    {
        Assert.True(ToolArguments.TryRange("2026-10-12", "2026-11-11", Today, out var range, out _, out _));

        Assert.Equal(new DateOnly(2026, 11, 12), range.EndExclusive);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), range.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), range.ToUtcExclusive);
    }

    [Fact]
    public void A_32_day_range_is_too_large()
    {
        Assert.False(ToolArguments.TryRange("2026-10-11", "2026-11-11", Today, out _, out var code, out var error));

        Assert.Equal(ToolErrorCode.RangeTooLarge, code);
        Assert.Equal("The range can be at most 31 days.", error);
    }

    // Nothing has happened after the investigation's current date, so a later end can only be a mistake.
    [Fact]
    public void A_range_can_t_end_after_today()
    {
        Assert.False(ToolArguments.TryRange("2026-11-10", "2026-11-12", Today, out _, out var code, out var error));

        Assert.Equal(ToolErrorCode.InvalidArgument, code);
        Assert.Equal("end_date can't be later than 2026-11-11.", error);
    }

    [Fact]
    public void A_single_day_range_ends_at_the_next_utc_midnight()
    {
        Assert.True(ToolArguments.TryRange("2026-11-09", "2026-11-09", Today, out var range, out _, out _));

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), range.ToUtcExclusive);
    }

    [Theory]
    [InlineData("2026-11-10", "2026-11-09")]
    [InlineData("2026-11-1", "2026-11-09")]
    [InlineData("11/09/2026", "2026-11-09")]
    [InlineData(null, "2026-11-09")]
    [InlineData("2026-11-09", "")]
    public void Malformed_or_reversed_dates_are_invalid(string? start, string? end)
    {
        Assert.False(ToolArguments.TryRange(start, end, Today, out _, out var code, out var error));

        Assert.Equal(ToolErrorCode.InvalidArgument, code);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(" M-1042 ", "M-1042")]
    [InlineData("m-1042", "m-1042")]
    [InlineData("fabrikam-pay", "fabrikam-pay")]
    [InlineData("SF-FAB-1105-R", "SF-FAB-1105-R")]
    public void Ids_lose_surrounding_whitespace_but_keep_their_case(string value, string expected)
    {
        Assert.True(ToolArguments.TryId(value, "merchant_id", out var id, out _));

        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("M 1042")]
    [InlineData("-M1042")]
    [InlineData("M-1042;DROP")]
    public void Malformed_ids_are_rejected_with_the_argument_named(string? value)
    {
        Assert.False(ToolArguments.TryId(value, "merchant_id", out _, out var error));

        Assert.StartsWith("merchant_id ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_over_64_characters_is_rejected() =>
        Assert.False(ToolArguments.TryId(new string('A', 65), "merchant_id", out _, out _));
}
