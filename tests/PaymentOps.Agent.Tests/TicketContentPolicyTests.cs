using PaymentOps.Agent.Investigating;

namespace PaymentOps.Agent.Tests;

public sealed class TicketContentPolicyTests
{
    [Theory]
    [InlineData("Card 4111 1111 1111 1111 declined")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("ref4111111111111111")]
    public void Card_numbers_are_found(string text) =>
        Assert.Contains("card_number", TicketContentPolicy.Violations(text));

    [Theory]
    [InlineData("Account 12345678")]
    [InlineData("IBAN GB82WEST12345698765432")]
    [InlineData("DE89370400440532013000")]
    [InlineData("IBAN GB29 NWBK 6016 1331 9268 19")]
    [InlineData("gb29nwbk60161331926819")]
    public void Account_like_numbers_are_found(string text) =>
        Assert.Contains("account_number", TicketContentPolicy.Violations(text));

    [Theory]
    [InlineData("PAY-6230-04")]
    [InlineData("2026-11-09")]
    [InlineData("FAB-7731")]
    [InlineData("PO-1042-NW-1109")]
    [InlineData("INC-FAB-1109")]
    [InlineData("1,234.56 USD")]
    [InlineData("AB12 batch settlement failed overnight")]
    [InlineData("fact:missing-file:fabrikam-pay:2026-11-09")]
    [InlineData("Settlement batch processing unavailable 2026-11-09 21:00 to 2026-11-10 02:00 UTC.")]
    public void Dataset_values_pass(string text) =>
        Assert.Empty(TicketContentPolicy.Violations(text));

    // A rule id is all a caller learns: the matched value never travels further.
    [Fact]
    public void Violations_never_contain_the_matched_text()
    {
        var violations = TicketContentPolicy.Violations("Customer card 4111111111111111, account 98765432");

        Assert.Equal(["card_number", "account_number"], violations);
        Assert.DoesNotContain(string.Concat(violations), char.IsDigit);
    }
}
